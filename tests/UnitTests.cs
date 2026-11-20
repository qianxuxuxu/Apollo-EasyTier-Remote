using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RemoteControlUI;

class UnitTests
{
    static int _passed = 0;
    static int _failed = 0;

    static void Assert(bool cond, string name)
    {
        if (cond)
        {
            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
        else
        {
            _failed++;
            Console.WriteLine("[FAIL] " + name);
        }
    }

    static int Main(string[] args)
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "rc_ui_unit_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        bool runLiveChecks = (args != null && Array.IndexOf(args, "--live") >= 0) ||
                             File.Exists(@"D:\远程控制\EasyTier\config.toml");
        bool skipNetwork = (args != null && Array.IndexOf(args, "--offline") >= 0);
        try
        {
            // 1. Missing config edge case
            EasyTierConfig missing = EasyTierConfig.Load(Path.Combine(tmpDir, "nonexistent.toml"));
            Assert(!missing.Exists, "Missing config sets Exists=false");
            Assert(missing.NoTun, "Missing config defaults NoTun=true");
            Assert(missing.HostIpOnly == "10.126.126.1", "Default HostIpOnly");
            Assert(missing.SuggestedTabletIp == "10.126.126.2", "Default SuggestedTabletIp");
            Assert(missing.SubnetCidr == "10.126.126.0/24", "Default SubnetCidr");

            // 2. Malformed, inline comments, single-quotes, escaped chars, multi-line listeners & no_tun=false coercion
            string dirtyPath = Path.Combine(tmpDir, "dirty.toml");
            File.WriteAllText(dirtyPath,
                "ipv4 = \"10.200.5.1/16\" # inline comment\n" +
                "dhcp = false\n" +
                "listeners = [\n" +
                "    \"tcp://0.0.0.0:11010\",\n" +
                "    \"udp://0.0.0.0:11010\",\n" +
                "]\n" +
                "[network_identity]\n" +
                "network_name = \"test-net-01\"\n" +
                "network_secret = 'Secret#Pass123456'\n" +
                "[[peer]]\nuri = \"tcp://1.2.3.4:11010\"\n" +
                "[flags]\nno_tun = false\n", new UTF8Encoding(false));

            EasyTierConfig loaded = EasyTierConfig.Load(dirtyPath);
            Assert(loaded.Exists, "Dirty TOML Exists=true");
            Assert(loaded.HostIpOnly == "10.200.5.1", "Inline comment stripped on ipv4");
            Assert(loaded.CidrPrefix == "/16", "Dynamic CIDR prefix parsed (/16)");
            Assert(loaded.SuggestedTabletIp == "10.200.5.2", "Dynamic tablet IP calculation");
            Assert(loaded.SubnetCidr == "10.200.5.0/16", "Dynamic subnet CIDR calculation with /16");
            Assert(loaded.Listeners.Count == 2 && loaded.Listeners[0] == "tcp://0.0.0.0:11010", "Multi-line listeners array parsed");
            Assert(loaded.NetworkName == "test-net-01", "Parsed network_name");
            Assert(loaded.NetworkSecret == "Secret#Pass123456", "Single-quoted secret with '#' preserved");
            Assert(loaded.NoTun == false, "Detected no_tun=false before save");

            // Host IP .2 collision avoidance test
            EasyTierConfig host2 = new EasyTierConfig { Ipv4 = "10.126.126.2/24" };
            Assert(host2.SuggestedTabletIp == "10.126.126.3", "SuggestedTabletIp avoids .2 collision -> .3");

            // Escaped quote & backslash TOML round-trip
            string escPath = Path.Combine(tmpDir, "escaped.toml");
            EasyTierConfig escCfg = new EasyTierConfig { NetworkName = "n\\a\"b", NetworkSecret = "s\\1\"2" };
            escCfg.Save(escPath);
            EasyTierConfig escReloaded = EasyTierConfig.Load(escPath);
            Assert(escReloaded.NetworkName == "n\\a\"b" && escReloaded.NetworkSecret == "s\\1\"2", "TOML escape/unescape round-trip exact");

            // Saving MUST force no_tun = true AND UTF-8 without BOM!
            string savedPath = Path.Combine(tmpDir, "saved.toml");
            loaded.Save(savedPath);
            byte[] rawBytes = File.ReadAllBytes(savedPath);
            Assert(!(rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF),
                "Saved config.toml has NO UTF-8 BOM");
            EasyTierConfig reloaded = EasyTierConfig.Load(savedPath);
            Assert(reloaded.NoTun == true, "Save enforces no_tun=true safety redline");
            Assert(reloaded.NetworkName == "test-net-01" && reloaded.NetworkSecret == "Secret#Pass123456",
                "Round-trip credentials preserved");
            string summary16 = BackendService.BuildPairingSummaryText(reloaded);
            Assert(summary16.Contains("10.200.5.2/16"), "Pairing summary uses dynamic CIDR prefix");

            // 3. UiSettings persistence edge cases
            string setPath = Path.Combine(tmpDir, "ui_settings.json");
            UiSettings defSet = UiSettings.Load(setPath);
            Assert(defSet.Theme == "Light" && defSet.AppTitle == "远程控制中心" && defSet.MinimizeToTrayOnClose,
                "Default UiSettings");
            defSet.Theme = "Dark";
            defSet.AppTitle = "我的专属远控";
            defSet.RefreshIntervalSeconds = 10;
            defSet.Save(setPath);
            UiSettings reSet = UiSettings.Load(setPath);
            Assert(reSet.Theme == "Dark" && reSet.AppTitle == "我的专属远控" && reSet.RefreshIntervalSeconds == 10,
                "UiSettings JSON round-trip");

            // 4. Random string generator entropy & length
            string r1 = BackendService.GenerateRandomString(20, "abcdefghijklmnopqrstuvwxyz0123456789");
            string r2 = BackendService.GenerateRandomString(20, "abcdefghijklmnopqrstuvwxyz0123456789");
            Assert(r1.Length == 20 && r2.Length == 20 && r1 != r2, "Crypto RNG generates unique 20-char secrets");

            // 5. URI parser & live TCP latency speedtest (reachable + invalid + timeout)
            string sc, h; int pt;
            Assert(BackendService.ParsePeerUri("tcp://225284.xyz:11010", out sc, out h, out pt) && sc == "tcp" && h == "225284.xyz" && pt == 11010,
                "ParsePeerUri valid tcp");
            Assert(!BackendService.ParsePeerUri("not a valid uri", out sc, out h, out pt), "ParsePeerUri rejects malformed");
            Assert(!BackendService.ParsePeerUri("", out sc, out h, out pt), "ParsePeerUri rejects empty");

            NodeSpeedResult spdBad = BackendService.TestNodeLatencyAsync("bad-uri", false).Result;
            Assert(!spdBad.Reachable && spdBad.StatusText == "格式无效", "Speedtest handles malformed URI");

            NodeSpeedResult spdRefused = BackendService.TestNodeLatencyAsync("tcp://127.0.0.1:1", false).Result;
            Assert(!spdRefused.Reachable && spdRefused.StatusText == "不可达", "Speedtest distinguishes refused/unreachable from timeout");

            if (!skipNetwork)
            {
                NodeSpeedResult spdLive = BackendService.TestNodeLatencyAsync("tcp://225284.xyz:11010", true).Result;
                Console.WriteLine("      Live node tcp://225284.xyz:11010 -> Reachable=" + spdLive.Reachable + ", Status=" + spdLive.StatusText);
                Assert(spdLive.Reachable && spdLive.LatencyMs > 0 && spdLive.LatencyMs < 4000, "Live public node TCP speedtest works");
            }
            else
            {
                Console.WriteLine("[SKIP] Live public node TCP speedtest (--offline mode)");
            }

            // 6. QR Encoder edge cases (empty string, short V1, medium V6, long V10+ > 255 bytes, UTF-8 Chinese, >V15 UTF-8 boundary truncation)
            bool[,] qrEmpty = QrCodeEncoder.Encode("");
            Assert(qrEmpty.GetLength(0) == 21, "QR empty string -> Version 1 (21x21)");
            bool[,] qrZh = QrCodeEncoder.Encode("远程控制测试: 10.126.126.1 / rc-test1234");
            Assert(qrZh.GetLength(0) >= 25, "QR UTF-8 Chinese encodes cleanly");
            string longPayload = new string('A', 300);
            bool[,] qrLong = QrCodeEncoder.Encode(longPayload);
            Assert(qrLong.GetLength(0) >= 65, "QR >255 bytes switches to 16-bit byte length indicator cleanly");
            string hugeZh = new string('中', 200); // 600 UTF-8 bytes > V15 limit
            bool[,] qrHugeZh = QrCodeEncoder.Encode(hugeZh);
            Assert(qrHugeZh.GetLength(0) == 77, "QR >V15 multi-byte UTF-8 truncates cleanly to V15 (77x77)");

            // 7. Live BackendService snapshot against real environment (skipped on clean CI runners)
            if (runLiveChecks)
            {
                BackendService svc = new BackendService();
                RuntimeSnapshot snap = svc.CollectSnapshot(true);
                Assert(snap.Config.Exists, "Real config.toml detected");
                Assert(snap.NoTunConfigOk && snap.NoTunAdapterOk, "Real no_tun config + 0 EasyTier adapters");
                Assert(snap.EasyTierRunning, "Real easytier-core.exe running");
                Assert(snap.AcNeverSleep, "Real powercfg AC never-sleep detected accurately on Chinese Windows");
                Assert(snap.DisplayAdapters.Count >= 2, "Real Win32_VideoController scan detected display adapters");
                Assert(!string.IsNullOrEmpty(snap.ApolloInstallerPath) && File.Exists(snap.ApolloInstallerPath), "Local Apollo installer detected");
                Assert(snap.ApolloInstalled && snap.ApolloRunning && snap.ApolloListening47990, "Real Apollo service & port 47990 detected");
                Assert(!string.IsNullOrEmpty(snap.ApolloExePath) && File.Exists(snap.ApolloExePath), "Real Apollo executable resolved on D:\\Apollo or service ImagePath");
                Assert(snap.EasyTierAutostart, "Real EasyTier-NoTun-Autostart task detected (even under non-admin context)");
            }
            else
            {
                Console.WriteLine("[SKIP] Live-host integration checks (no D:\\远程控制\\EasyTier\\config.toml on clean runner)");
            }

            // 8. Native Task XML builder validation (zero PowerShell -EncodedCommand)
            string taskXml = BackendService.BuildScheduledTaskXml(
                "S-1-5-18", "ServiceAccount", true, true, false, "IgnoreNew",
                @"D:\远程控制\EasyTier\easytier-core.exe",
                "-c \"D:\\远程控制\\EasyTier\\config.toml\" --console-log-level warn",
                @"D:\远程控制\EasyTier");
            System.Xml.XmlDocument xdoc = new System.Xml.XmlDocument();
            xdoc.LoadXml(taskXml);
            Assert(taskXml.Contains("--console-log-level warn") && taskXml.Contains("HighestAvailable") && taskXml.Contains("S-1-5-18") && taskXml.Contains("ServiceAccount"),
                "BuildScheduledTaskXml produces valid well-formed Task Scheduler 1.2 XML");

            // 9. Adapter classification (no false positive on Clash/Mihomo/FlClash TUN, catches EasyTier Wintun)
            Assert(BackendService.IsEasyTierVirtualAdapter("et_0", "Wintun Userspace Tunnel"), "Catches et_0 Wintun adapter");
            Assert(BackendService.IsEasyTierVirtualAdapter("EasyTier Virtual NIC", "Wintun"), "Catches EasyTier named adapter");
            Assert(BackendService.IsEasyTierVirtualAdapter("UnknownTunnel", "Wintun Userspace Tunnel"), "Catches unattributed Wintun adapter");
            Assert(!BackendService.IsEasyTierVirtualAdapter("Mihomo", "Wintun Userspace Tunnel"), "Does NOT false-positive Mihomo proxy Wintun");
            Assert(!BackendService.IsEasyTierVirtualAdapter("FlClash", "Wintun Userspace Tunnel"), "Does NOT false-positive FlClash proxy Wintun");
            Assert(!BackendService.IsEasyTierVirtualAdapter("Anycast VPN", "Anycast VPN Tunnel"), "Does NOT false-positive Anycast VPN");

            // 10. Task Scheduler SDDL canonical hex mask check (FRFX -> 0x1200a9 idempotency)
            string testSid = "S-1-5-21-111-222-333-1001";
            Assert(!BackendService.HasTaskExecuteAce("D:(A;;FR;;;" + testSid + ")", testSid), "Read-only FR ACE does not satisfy execute check");
            Assert(BackendService.HasTaskExecuteAce("D:(A;;FRFX;;;" + testSid + ")", testSid), "FRFX ACE satisfies execute check");
            Assert(BackendService.HasTaskExecuteAce("D:AI(A;;FR;;;" + testSid + ")(A;;0x1200a9;;;" + testSid + ")", testSid), "Windows canonical 0x1200a9 ACE satisfies execute check");

            // 11. Private vs remote host classification (for proxy TUN fake 1-2ms latency detection)
            Assert(BackendService.IsPrivateOrLoopbackHost("127.0.0.1") && BackendService.IsPrivateOrLoopbackHost("10.126.126.1") && BackendService.IsPrivateOrLoopbackHost("192.168.1.10"),
                "Recognizes loopback & RFC1918 private hosts");
            Assert(!BackendService.IsPrivateOrLoopbackHost("225284.xyz") && !BackendService.IsPrivateOrLoopbackHost("183.230.36.171"),
                "Recognizes public EasyTier node hosts");

            Console.WriteLine(string.Format("=== UNIT TEST SUMMARY: {0} passed, {1} failed ===", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }
}
