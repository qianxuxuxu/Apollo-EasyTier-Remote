using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace RemoteControlUI
{
    public class ActionResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; }

        public ActionResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }
    }

    public class BackendService
    {
        public static readonly string[] BuiltinPublicNodes = new string[]
        {
            "tcp://225284.xyz:11010",
            "tcp://easytier.weiai.org.cn:11010",
            "tcp://183.230.36.171:11010",
            "tcp://et.sumnight.top:11010"
        };

        public string WorkspaceRoot { get; private set; }
        public string EasyTierDir { get { return Path.Combine(WorkspaceRoot, "EasyTier"); } }
        public string CoreExePath { get { return Path.Combine(EasyTierDir, "easytier-core.exe"); } }
        public string CliExePath { get { return Path.Combine(EasyTierDir, "easytier-cli.exe"); } }
        public string ConfigTomlPath { get { return Path.Combine(EasyTierDir, "config.toml"); } }
        public string SettingsPath { get { return Path.Combine(WorkspaceRoot, "ui_settings.json"); } }

        public BackendService()
        {
            WorkspaceRoot = ResolveWorkspaceRoot();
        }

        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static string ResolveWorkspaceRoot()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            List<string> candidates = new List<string>();
            candidates.Add(exeDir);
            try
            {
                DirectoryInfo parent = Directory.GetParent(exeDir);
                if (parent != null) candidates.Add(parent.FullName);
            }
            catch { }
            candidates.Add(@"D:\远程控制");
            candidates.Add(@"C:\远程控制");
            try
            {
                string user = Environment.UserName;
                if (!string.IsNullOrEmpty(user))
                {
                    candidates.Add(Path.Combine(@"D:\", user, "远程控制"));
                }
            }
            catch { }

            for (int i = 0; i < candidates.Count; i++)
            {
                string c = candidates[i];
                if (File.Exists(Path.Combine(c, @"EasyTier\config.toml")) ||
                    File.Exists(Path.Combine(c, @"EasyTier\easytier-core.exe")))
                {
                    return c;
                }
            }
            return exeDir;
        }

        public EasyTierConfig LoadConfig()
        {
            return EasyTierConfig.Load(ConfigTomlPath);
        }

        public RuntimeSnapshot CollectSnapshot(bool includeSlowChecks)
        {
            RuntimeSnapshot snap = new RuntimeSnapshot();
            snap.Timestamp = DateTime.Now;
            snap.IsAdmin = IsAdministrator();
            snap.WorkspaceRoot = WorkspaceRoot;
            snap.Config = LoadConfig();
            snap.NoTunConfigOk = snap.Config.NoTun;
            snap.EasyTierInstalled = File.Exists(CoreExePath);

            // 1. EasyTier Process
            try
            {
                Process[] procs = Process.GetProcessesByName("easytier-core");
                try
                {
                    snap.EasyTierRunning = procs.Length > 0;
                    if (procs.Length > 0)
                    {
                        List<string> pids = new List<string>();
                        for (int i = 0; i < procs.Length; i++) pids.Add(procs[i].Id.ToString());
                        snap.EasyTierPids = string.Join(", ", pids.ToArray());
                    }
                }
                finally
                {
                    for (int i = 0; i < procs.Length; i++)
                    {
                        try { procs[i].Dispose(); } catch { }
                    }
                }
            }
            catch { }

            // 2. EasyTier CLI node & peer JSON
            if (snap.EasyTierRunning && File.Exists(CliExePath))
            {
                QueryEasyTierCli(snap);
            }
            else
            {
                snap.SummaryMode = "Offline";
                snap.SummaryLatency = "--";
            }

            // 3. Apollo Service & Port 47990
            InspectApollo(snap);

            // 4. No-TUN Network Adapter Check
            InspectNetworkAdapters(snap);

            // 5. Firewall, Scheduled Tasks, PowerCfg, Video Adapters (parallelized for speed)
            if (includeSlowChecks)
            {
                Task<FirewallAuditResult> tFw = Task.Run(() => AuditFirewall());
                Task<bool> tEtAuto = Task.Run(() => CheckTaskExists("EasyTier-NoTun-Autostart", true) || CheckTaskExists("EasyTier-RemoteControl", true));
                Task<bool> tUiAuto = Task.Run(() => CheckTaskExists("RemoteControlUI-Autostart", true));
                Task<bool> tElev = Task.Run(() => CheckTaskExists("RemoteControlUI-ElevatedLaunch", false));
                Task<bool> tAc = Task.Run(() => CheckAcNeverSleep());
                Task<List<DisplayAdapterInfo>> tDisp = Task.Run(() => ScanDisplayAdapters());

                try { Task.WaitAll(new Task[] { tFw, tEtAuto, tUiAuto, tElev, tAc, tDisp }, 10000); } catch { }

                if (tFw.IsCompleted && !tFw.IsFaulted) snap.Firewall = tFw.Result;
                if (tEtAuto.IsCompleted && !tEtAuto.IsFaulted) snap.EasyTierAutostart = tEtAuto.Result;
                if (tUiAuto.IsCompleted && !tUiAuto.IsFaulted) snap.UiAutostart = tUiAuto.Result;
                if (tElev.IsCompleted && !tElev.IsFaulted) snap.ElevatedTaskReady = tElev.Result;
                if (tAc.IsCompleted && !tAc.IsFaulted) snap.AcNeverSleep = tAc.Result;
                if (tDisp.IsCompleted && !tDisp.IsFaulted) snap.DisplayAdapters = tDisp.Result;
            }

            return snap;
        }

        private void QueryEasyTierCli(RuntimeSnapshot snap)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            // node
            try
            {
                string nodeOut = RunProcessCapture(CliExePath, "-o json node", EasyTierDir, 3000, Encoding.UTF8);
                if (!string.IsNullOrEmpty(nodeOut) && nodeOut.TrimStart().StartsWith("{"))
                {
                    Dictionary<string, object> obj = ser.DeserializeObject(nodeOut) as Dictionary<string, object>;
                    if (obj != null)
                    {
                        snap.EasyTierNodeIp = GetDictStr(obj, "ipv4_addr");
                        snap.EasyTierVersion = GetDictStr(obj, "version");
                        snap.EasyTierHostname = GetDictStr(obj, "hostname");
                    }
                }
            }
            catch { }

            // peer
            try
            {
                string peerOut = RunProcessCapture(CliExePath, "-o json peer", EasyTierDir, 3000, Encoding.UTF8);
                if (!string.IsNullOrEmpty(peerOut) && peerOut.TrimStart().StartsWith("["))
                {
                    object[] arr = ser.DeserializeObject(peerOut) as object[];
                    if (arr != null)
                    {
                        bool hasP2p = false;
                        bool hasRelay = false;
                        double bestLat = double.MaxValue;
                        int publicCount = 0;
                        double bestPublicLat = double.MaxValue;

                        for (int i = 0; i < arr.Length; i++)
                        {
                            Dictionary<string, object> row = arr[i] as Dictionary<string, object>;
                            if (row == null) continue;
                            PeerNodeInfo info = new PeerNodeInfo();
                            info.Hostname = GetDictStr(row, "hostname");
                            string ipv4 = GetDictStr(row, "ipv4");
                            string cidr = GetDictStr(row, "cidr");
                            info.VirtualIp = !string.IsNullOrEmpty(ipv4) ? ipv4 : cidr;
                            info.CostMode = GetDictStr(row, "cost");
                            info.LatencyText = GetDictStr(row, "lat_ms");
                            info.LossRate = GetDictStr(row, "loss_rate");
                            info.TunnelProto = GetDictStr(row, "tunnel_proto");
                            info.Version = GetDictStr(row, "version");
                            info.IsLocal = string.Equals(info.CostMode, "Local", StringComparison.OrdinalIgnoreCase);
                            string hostLow = (info.Hostname ?? "").ToLowerInvariant();
                            bool noVirtualIp = string.IsNullOrEmpty(info.VirtualIp) || info.VirtualIp == "-";
                            info.IsPublicServer = !info.IsLocal && (noVirtualIp || hostLow.StartsWith("publicserver") || hostLow.StartsWith("public_server"));

                            string latClean = (info.LatencyText ?? "").Trim();
                            if (latClean.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
                            {
                                latClean = latClean.Substring(0, latClean.Length - 2).Trim();
                            }
                            double parsedLat;
                            if (double.TryParse(latClean, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedLat))
                            {
                                info.LatencyMs = parsedLat;
                                info.LatencyText = string.Format("{0:F1} ms", parsedLat);
                                if (!info.IsLocal && !info.IsPublicServer && parsedLat < bestLat) bestLat = parsedLat;
                                if (info.IsPublicServer && parsedLat < bestPublicLat) bestPublicLat = parsedLat;
                            }
                            else
                            {
                                info.LatencyMs = -1;
                            }

                            if (info.IsPublicServer)
                            {
                                publicCount++;
                            }
                            else if (!info.IsLocal)
                            {
                                string lowCost = (info.CostMode ?? "").ToLowerInvariant();
                                if (lowCost.Contains("p2p") || lowCost == "direct" || lowCost == "1")
                                    hasP2p = true;
                                else if (lowCost.Contains("relay"))
                                    hasRelay = true;
                            }
                            snap.Peers.Add(info);
                        }

                        if (hasP2p)
                        {
                            snap.SummaryMode = "P2P";
                            snap.SummaryLatency = bestLat < double.MaxValue ? string.Format("{0:F1} ms", bestLat) : "直连中";
                        }
                        else if (hasRelay)
                        {
                            snap.SummaryMode = "Relay";
                            snap.SummaryLatency = bestLat < double.MaxValue ? string.Format("{0:F1} ms", bestLat) : "中继中";
                        }
                        else if (publicCount > 0)
                        {
                            snap.SummaryMode = "PublicReady";
                            snap.SummaryLatency = bestPublicLat < double.MaxValue
                                ? string.Format("公共节点 {0:F1} ms", bestPublicLat)
                                : string.Format("已接入 {0} 个公共节点", publicCount);
                        }
                        else
                        {
                            snap.SummaryMode = "LocalOnly";
                            snap.SummaryLatency = "等待对端接入";
                        }
                    }
                }
                else
                {
                    snap.SummaryMode = "LocalOnly";
                    snap.SummaryLatency = "等待对端接入";
                }
            }
            catch
            {
                snap.SummaryMode = "LocalOnly";
                snap.SummaryLatency = "--";
            }
        }

        private void InspectApollo(RuntimeSnapshot snap)
        {
            // 1. Check TCP port 47990 / 47989 / 47984 listening
            try
            {
                IPEndPoint[] eps = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                for (int i = 0; i < eps.Length; i++)
                {
                    if (eps[i].Port == 47990)
                    {
                        snap.ApolloListening47990 = true;
                        snap.ApolloRunning = true;
                        snap.ApolloInstalled = true;
                        break;
                    }
                    if (eps[i].Port == 47989 || eps[i].Port == 47984)
                    {
                        snap.ApolloRunning = true;
                        snap.ApolloInstalled = true;
                    }
                }
            }
            catch { }

            // 2. Check Windows Services and resolve install path from Service ImagePath registry key
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                try
                {
                    for (int i = 0; i < services.Length; i++)
                    {
                        ServiceController sc = services[i];
                        string n = (sc.ServiceName ?? "").ToLowerInvariant();
                        string d = (sc.DisplayName ?? "").ToLowerInvariant();
                        if (n.Contains("apollo") || n.Contains("sunshine") || d.Contains("apollo") || d.Contains("sunshine"))
                        {
                            snap.ApolloServiceName = sc.ServiceName;
                            snap.ApolloInstalled = true;
                            if (sc.Status == ServiceControllerStatus.Running)
                            {
                                snap.ApolloRunning = true;
                            }
                            string svcExe = ResolveApolloExeFromService(sc.ServiceName);
                            if (!string.IsNullOrEmpty(svcExe))
                            {
                                snap.ApolloExePath = svcExe;
                            }
                            break;
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < services.Length; i++)
                    {
                        try { services[i].Dispose(); } catch { }
                    }
                }
            }
            catch { }

            // 3. Check running processes (apollo / sunshine / sunshinesvc)
            if (!snap.ApolloRunning)
            {
                string[] procNames = new string[] { "apollo", "sunshine", "sunshinesvc" };
                for (int k = 0; k < procNames.Length; k++)
                {
                    try
                    {
                        Process[] ps = Process.GetProcessesByName(procNames[k]);
                        try
                        {
                            if (ps.Length > 0)
                            {
                                snap.ApolloRunning = true;
                                snap.ApolloInstalled = true;
                            }
                        }
                        finally
                        {
                            for (int i = 0; i < ps.Length; i++) { try { ps[i].Dispose(); } catch { } }
                        }
                    }
                    catch { }
                }
            }

            // 4. Check common install paths across C: and D: (and Registry Uninstall keys)
            if (string.IsNullOrEmpty(snap.ApolloExePath) || !File.Exists(snap.ApolloExePath))
            {
                string[] cands = new string[]
                {
                    @"D:\Apollo\sunshine.exe",
                    @"D:\Apollo\apollo.exe",
                    @"D:\Sunshine\sunshine.exe",
                    @"C:\Program Files\Apollo\sunshine.exe",
                    @"C:\Program Files\Apollo\apollo.exe",
                    @"C:\Program Files\Sunshine\sunshine.exe",
                    @"C:\Program Files (x86)\Apollo\sunshine.exe"
                };
                for (int i = 0; i < cands.Length; i++)
                {
                    if (File.Exists(cands[i]))
                    {
                        snap.ApolloExePath = cands[i];
                        snap.ApolloInstalled = true;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(snap.ApolloExePath))
            {
                string regExe = ResolveApolloExeFromUninstallRegistry();
                if (!string.IsNullOrEmpty(regExe))
                {
                    snap.ApolloExePath = regExe;
                    snap.ApolloInstalled = true;
                }
            }

            // 5. Check local Apollo installer path
            try
            {
                string apolloDir = Path.Combine(WorkspaceRoot, "Apollo");
                if (Directory.Exists(apolloDir))
                {
                    string[] installers = Directory.GetFiles(apolloDir, "*Apollo*.exe");
                    if (installers.Length > 0)
                    {
                        Array.Sort(installers, StringComparer.OrdinalIgnoreCase);
                        snap.ApolloInstallerPath = installers[installers.Length - 1];
                    }
                }
            }
            catch { }
        }

        private static string ResolveApolloExeFromService(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName)) return "";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName))
                {
                    if (key == null) return "";
                    string imgPath = key.GetValue("ImagePath") as string;
                    if (string.IsNullOrEmpty(imgPath)) return "";
                    string exePath = ExtractExecutableFromCommandLine(imgPath);
                    if (string.IsNullOrEmpty(exePath)) return "";

                    // Service ImagePath may point to D:\Apollo\tools\sunshinesvc.exe -> check same dir and parent dir
                    string dir = Path.GetDirectoryName(exePath);
                    string[] dirs = new string[] { dir, dir != null ? Path.GetDirectoryName(dir) : null };
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        if (string.IsNullOrEmpty(dirs[i])) continue;
                        string s1 = Path.Combine(dirs[i], "sunshine.exe");
                        if (File.Exists(s1)) return s1;
                        string s2 = Path.Combine(dirs[i], "apollo.exe");
                        if (File.Exists(s2)) return s2;
                    }
                    if (File.Exists(exePath)) return exePath;
                }
            }
            catch { }
            return "";
        }

        private static string ResolveApolloExeFromUninstallRegistry()
        {
            string[] roots = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            for (int r = 0; r < roots.Length; r++)
            {
                try
                {
                    using (RegistryKey baseKey = Registry.LocalMachine.OpenSubKey(roots[r]))
                    {
                        if (baseKey == null) continue;
                        string[] subNames = baseKey.GetSubKeyNames();
                        for (int i = 0; i < subNames.Length; i++)
                        {
                            try
                            {
                                using (RegistryKey sk = baseKey.OpenSubKey(subNames[i]))
                                {
                                    if (sk == null) continue;
                                    string dn = (sk.GetValue("DisplayName") as string) ?? "";
                                    string low = dn.ToLowerInvariant();
                                    if (!low.Contains("apollo") && !low.Contains("sunshine")) continue;
                                    string loc = (sk.GetValue("InstallLocation") as string) ?? "";
                                    if (!string.IsNullOrEmpty(loc))
                                    {
                                        string p1 = Path.Combine(loc.Trim('"'), "sunshine.exe");
                                        if (File.Exists(p1)) return p1;
                                        string p2 = Path.Combine(loc.Trim('"'), "apollo.exe");
                                        if (File.Exists(p2)) return p2;
                                    }
                                    string uninst = (sk.GetValue("UninstallString") as string) ?? "";
                                    string uExe = ExtractExecutableFromCommandLine(uninst);
                                    if (!string.IsNullOrEmpty(uExe))
                                    {
                                        string uDir = Path.GetDirectoryName(uExe);
                                        if (!string.IsNullOrEmpty(uDir))
                                        {
                                            string p1 = Path.Combine(uDir, "sunshine.exe");
                                            if (File.Exists(p1)) return p1;
                                            string p2 = Path.Combine(uDir, "apollo.exe");
                                            if (File.Exists(p2)) return p2;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return "";
        }

        private static string ExtractExecutableFromCommandLine(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return "";
            string s = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (s.StartsWith("\""))
            {
                int endQ = s.IndexOf('"', 1);
                if (endQ > 1) return s.Substring(1, endQ - 1);
            }
            int exeIdx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
            {
                return s.Substring(0, exeIdx + 4).Trim('"');
            }
            return s;
        }

        public static bool IsEasyTierVirtualAdapter(string name, string description)
        {
            string n = (name ?? "").Trim();
            string d = (description ?? "").Trim();
            string combined = (n + " " + d).ToLowerInvariant();
            if (combined.Contains("easytier")) return true;
            if (Regex.IsMatch(n, @"^et[_-]?\d+", RegexOptions.IgnoreCase)) return true;
            if (combined.Contains("wintun"))
            {
                string[] knownProxyTokens = new string[]
                {
                    "clash", "mihomo", "flclash", "meta", "sing-box", "singbox",
                    "wireguard", "tailscale", "anycast", "proton", "letsvpn",
                    "nekoray", "v2ray", "shadowsocks", "zerotier", "openclash"
                };
                for (int i = 0; i < knownProxyTokens.Length; i++)
                {
                    if (combined.Contains(knownProxyTokens[i])) return false;
                }
                return true;
            }
            return false;
        }

        private void InspectNetworkAdapters(RuntimeSnapshot snap)
        {
            snap.NoTunAdapterOk = true;
            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length; i++)
                {
                    string n = nics[i].Name ?? "";
                    string d = nics[i].Description ?? "";
                    if (IsEasyTierVirtualAdapter(n, d))
                    {
                        snap.NoTunAdapterOk = false;
                        snap.BadVirtualAdapters.Add(n + " (" + d + ")");
                    }
                }
            }
            catch { }
        }

        public FirewallAuditResult AuditFirewall()
        {
            FirewallAuditResult res = new FirewallAuditResult();
            string[][] ruleGroups = new string[][]
            {
                new string[] { "Apollo-Stream-TCP-In", "Apollo-TCP-In" },
                new string[] { "Apollo-Stream-UDP-In", "Apollo-UDP-In" },
                new string[] { "EasyTier-P2P-Program-In", "EasyTier-Core-Program" },
                new string[] { "EasyTier-P2P-TCP-In", "EasyTier-TCP-11010" },
                new string[] { "EasyTier-P2P-UDP-In", "EasyTier-UDP-11010" }
            };
            res.TotalCount = ruleGroups.Length;
            res.PassedCount = 0;

            string[] matchedNames = new string[ruleGroups.Length];
            Task[] tasks = new Task[ruleGroups.Length];
            for (int idx = 0; idx < ruleGroups.Length; idx++)
            {
                int i = idx;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < ruleGroups[i].Length; j++)
                    {
                        string ruleName = ruleGroups[i][j];
                        int exitCode;
                        string outStr = RunProcessCaptureWithCode("netsh.exe", string.Format("advfirewall firewall show rule name=\"{0}\"", ruleName), WorkspaceRoot, 2500, out exitCode, Encoding.Default);
                        if (exitCode == 0 && IsFirewallRuleActiveAllow(outStr))
                        {
                            matchedNames[i] = ruleName;
                            break;
                        }
                    }
                });
            }
            try { Task.WaitAll(tasks, 6000); } catch { }

            for (int i = 0; i < ruleGroups.Length; i++)
            {
                if (!string.IsNullOrEmpty(matchedNames[i]))
                {
                    res.PassedCount++;
                    res.PresentRules.Add(matchedNames[i]);
                }
                else
                {
                    res.MissingRules.Add(ruleGroups[i][0]);
                }
            }
            return res;
        }

        private static bool IsFirewallRuleActiveAllow(string netshOutput)
        {
            if (string.IsNullOrEmpty(netshOutput)) return false;
            if (Regex.IsMatch(netshOutput, @"(?:Enabled|已启用)\s*:\s*(?:No|否)\b", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(netshOutput, @"(?:Action|操作)\s*:\s*(?:Block|阻止)\b", RegexOptions.IgnoreCase)) return false;
            return true;
        }

        public ActionResult RepairFirewallRules()
        {
            if (!IsAdministrator())
            {
                return new ActionResult(false, "需要管理员权限才能写入 Windows 防火墙规则。");
            }
            try
            {
                string[] oldRules = new string[]
                {
                    "Apollo-Stream-TCP-In", "Apollo-Stream-UDP-In", "Apollo-TCP-In", "Apollo-UDP-In",
                    "EasyTier-P2P-Program-In", "EasyTier-P2P-TCP-In", "EasyTier-P2P-UDP-In",
                    "EasyTier-Core-Program", "EasyTier-TCP-11010", "EasyTier-UDP-11010"
                };
                for (int i = 0; i < oldRules.Length; i++)
                {
                    int c;
                    RunProcessCaptureWithCode("netsh.exe", string.Format("advfirewall firewall delete rule name=\"{0}\"", oldRules[i]), WorkspaceRoot, 3000, out c);
                }

                List<string> cmds = new List<string>
                {
                    "advfirewall firewall add rule name=\"Apollo-Stream-TCP-In\" dir=in action=allow protocol=TCP localport=47984,47989,47990,48010 profile=any",
                    "advfirewall firewall add rule name=\"Apollo-Stream-UDP-In\" dir=in action=allow protocol=UDP localport=47998-48000,48002,48010 profile=any",
                    string.Format("advfirewall firewall add rule name=\"EasyTier-P2P-Program-In\" dir=in action=allow program=\"{0}\" profile=any", CoreExePath),
                    "advfirewall firewall add rule name=\"EasyTier-P2P-TCP-In\" dir=in action=allow protocol=TCP localport=11010 profile=any",
                    "advfirewall firewall add rule name=\"EasyTier-P2P-UDP-In\" dir=in action=allow protocol=UDP localport=11010 profile=any"
                };

                for (int i = 0; i < cmds.Count; i++)
                {
                    int code;
                    string outStr = RunProcessCaptureWithCode("netsh.exe", cmds[i], WorkspaceRoot, 4000, out code);
                    if (code != 0)
                    {
                        return new ActionResult(false, "防火墙规则写入失败: " + outStr);
                    }
                }
                return new ActionResult(true, "防火墙 5 条入站放行规则已全部修复并生效！");
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "防火墙修复异常: " + ex.Message);
            }
        }

        public bool CheckTaskExists(string taskName, bool treatAccessDeniedAsExists = true)
        {
            int code;
            string outStr = RunProcessCaptureWithCode("schtasks.exe", string.Format("/query /tn \"{0}\"", taskName), WorkspaceRoot, 2500, out code);
            if (code == 0) return true;
            // SYSTEM-owned task returns Access Denied to non-elevated callers, proving the task exists.
            // User-runnable tasks (like RemoteControlUI-ElevatedLaunch) pass treatAccessDeniedAsExists=false.
            if (treatAccessDeniedAsExists && !string.IsNullOrEmpty(outStr) &&
                (outStr.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0 || outStr.Contains("拒绝访问")))
            {
                return true;
            }
            return false;
        }

        public bool CheckAcNeverSleep()
        {
            try
            {
                int code;
                string txt = RunProcessCaptureWithCode("powercfg.exe", "/query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE", WorkspaceRoot, 2500, out code, Encoding.Default);
                if (code != 0 || string.IsNullOrEmpty(txt)) return false;
                string[] lines = txt.Replace("\r\n", "\n").Split('\n');
                List<int> hexVals = new List<int>();
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i].Trim();
                    Match m = Regex.Match(l, "0x([0-9a-fA-F]{8})");
                    if (l.Contains("交流电源") || l.IndexOf("Current AC Power Setting Index", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (m.Success)
                        {
                            return Convert.ToInt32(m.Groups[1].Value, 16) == 0;
                        }
                    }
                    if (m.Success)
                    {
                        hexVals.Add(Convert.ToInt32(m.Groups[1].Value, 16));
                    }
                }
                // Fallback: powercfg /query always ends with [AC_index, DC_index]
                if (hexVals.Count >= 2)
                {
                    return hexVals[hexVals.Count - 2] == 0;
                }
            }
            catch { }
            return false;
        }

        public ActionResult SetAcNeverSleep(bool enable)
        {
            try
            {
                int minutes = enable ? 0 : 30;
                int code;
                string outStr = RunProcessCaptureWithCode("powercfg.exe", string.Format("/change standby-timeout-ac {0}", minutes), WorkspaceRoot, 3000, out code);
                if (code == 0)
                {
                    return new ActionResult(true, enable ? "已开启接通电源永不休眠 (standby-timeout-ac = 0)。" : "已恢复接通电源 30 分钟后自动休眠。");
                }
                return new ActionResult(false, "设置电源策略失败: " + outStr);
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "电源策略异常: " + ex.Message);
            }
        }

        public List<DisplayAdapterInfo> ScanDisplayAdapters()
        {
            List<DisplayAdapterInfo> list = new List<DisplayAdapterInfo>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, Status, PNPDeviceID FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"] != null ? obj["Name"].ToString() : "Unknown Display";
                        string status = obj["Status"] != null ? obj["Status"].ToString() : "";
                        string pnp = obj["PNPDeviceID"] != null ? obj["PNPDeviceID"].ToString() : "";
                        string low = (name + " " + pnp).ToLowerInvariant();

                        string cat = "Physical";
                        if (low.Contains("sudovda") || low.Contains("apollo") || low.Contains("sudomaker"))
                        {
                            cat = "ApolloVDA";
                        }
                        else if (low.Contains("virtual") || low.Contains("mumu") || low.Contains("todesk") ||
                                 low.Contains("gameviewer") || low.Contains("parsec") || low.Contains("iddsample") ||
                                 low.Contains("oray") || pnp.StartsWith("ROOT\\DISPLAY", StringComparison.OrdinalIgnoreCase))
                        {
                            cat = "ThirdPartyVirtual";
                        }

                        list.Add(new DisplayAdapterInfo
                        {
                            Name = name,
                            Status = status,
                            PnpId = pnp,
                            Category = cat
                        });
                    }
                }
            }
            catch { }
            return list;
        }

        public ActionResult StartEasyTier()
        {
            try
            {
                if (IsProcessRunning("easytier-core"))
                {
                    return new ActionResult(true, "EasyTier 核心服务已在运行中 (no_tun = true)。");
                }
                if (!File.Exists(CoreExePath))
                {
                    return new ActionResult(false, "未找到 easytier-core.exe: " + CoreExePath);
                }
                EasyTierConfig cfg = LoadConfig();
                if (!cfg.Exists)
                {
                    InitializeFreshConfig();
                }
                else if (!cfg.NoTun)
                {
                    cfg.NoTun = true;
                    cfg.Save(ConfigTomlPath);
                }

                if (CheckTaskExists("EasyTier-NoTun-Autostart"))
                {
                    int c;
                    RunProcessCaptureWithCode("schtasks.exe", "/run /tn \"EasyTier-NoTun-Autostart\"", WorkspaceRoot, 4000, out c);
                    Thread.Sleep(2500);
                    if (IsProcessRunning("easytier-core"))
                    {
                        return new ActionResult(true, "EasyTier 已通过系统计划任务启动 (no_tun = true)。");
                    }
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = CoreExePath;
                psi.Arguments = string.Format("-c \"{0}\" --console-log-level warn", ConfigTomlPath);
                psi.WorkingDirectory = EasyTierDir;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                using (Process started = Process.Start(psi)) { }
                Thread.Sleep(2500);

                if (IsProcessRunning("easytier-core"))
                {
                    return new ActionResult(true, "EasyTier 后台组网核心已启动 (no_tun = true)。");
                }
                return new ActionResult(false, "EasyTier 启动后未检测到驻留进程，请检查 config.toml。");
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "启动 EasyTier 失败: " + ex.Message);
            }
        }

        public ActionResult StopEasyTier()
        {
            try
            {
                // End scheduled task first so RestartOnFailure does not automatically restart easytier-core after 1 minute
                if (CheckTaskExists("EasyTier-NoTun-Autostart", true))
                {
                    int tc;
                    RunProcessCaptureWithCode("schtasks.exe", "/end /tn \"EasyTier-NoTun-Autostart\"", WorkspaceRoot, 3000, out tc);
                    Thread.Sleep(400);
                }

                Process[] procs = Process.GetProcessesByName("easytier-core");
                string lastErr = null;
                try
                {
                    if (procs.Length == 0)
                    {
                        return new ActionResult(true, "EasyTier 已停止。");
                    }
                    for (int i = 0; i < procs.Length; i++)
                    {
                        try
                        {
                            procs[i].Kill();
                            procs[i].WaitForExit(3000);
                        }
                        catch (Exception ex)
                        {
                            lastErr = ex.Message;
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < procs.Length; i++) { try { procs[i].Dispose(); } catch { } }
                }
                Thread.Sleep(600);
                if (IsProcessRunning("easytier-core"))
                {
                    return new ActionResult(false, "未能停止 EasyTier 进程（可能需要管理员权限）" + (string.IsNullOrEmpty(lastErr) ? "" : ": " + lastErr));
                }
                return new ActionResult(true, "已停止 EasyTier 后台进程。");
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "停止 EasyTier 失败: " + ex.Message);
            }
        }

        public ActionResult RestartEasyTier()
        {
            ActionResult stopRes = StopEasyTier();
            if (!stopRes.Ok && IsProcessRunning("easytier-core"))
            {
                return stopRes;
            }
            return StartEasyTier();
        }

        public ActionResult RepairNoTunConfig()
        {
            try
            {
                EasyTierConfig cfg = LoadConfig();
                cfg.NoTun = true;
                cfg.Save(ConfigTomlPath);
                if (File.Exists(CoreExePath))
                {
                    return RestartEasyTier();
                }
                return new ActionResult(true, "已强制修复 config.toml 为 no_tun = true。");
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "修复 no_tun 配置失败: " + ex.Message);
            }
        }

        public ActionResult InstallApolloSilent()
        {
            if (!IsAdministrator())
            {
                return new ActionResult(false, "安装 Apollo 串流服务端需要管理员权限。");
            }
            try
            {
                RuntimeSnapshot s = new RuntimeSnapshot();
                InspectApollo(s);
                if (s.ApolloListening47990)
                {
                    return new ActionResult(true, "Apollo 服务端已在运行并监听端口 47990。");
                }
                if (string.IsNullOrEmpty(s.ApolloInstallerPath) || !File.Exists(s.ApolloInstallerPath))
                {
                    return new ActionResult(false, "未在本地 Apollo 目录找到安装包 (Apollo-*.exe)。");
                }
                // 使用交互向导而非 /S，避免静默安装 ViGEmBus (/promptrestart) 导致 Windows PnP 驱动锁阻断代理软件 Wintun 虚拟网卡
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = s.ApolloInstallerPath,
                    WorkingDirectory = Path.GetDirectoryName(s.ApolloInstallerPath),
                    UseShellExecute = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p != null) p.WaitForExit();
                }
                Thread.Sleep(2500);
                RuntimeSnapshot after = new RuntimeSnapshot();
                InspectApollo(after);
                if (!string.IsNullOrEmpty(after.ApolloServiceName) && !after.ApolloRunning)
                {
                    ControlApollo("start");
                    Thread.Sleep(2000);
                    InspectApollo(after);
                }
                if (after.ApolloListening47990 || after.ApolloRunning)
                {
                    return new ActionResult(true, "Apollo 服务端安装完成并已启动（端口 47990 就绪）！");
                }
                if (after.ApolloInstalled)
                {
                    return new ActionResult(true, "Apollo 安装程序已完成，请点击启动按钮或检查服务状态。");
                }
                return new ActionResult(false, "Apollo 安装向导已关闭，暂未检测到 47990 服务监听。");
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "启动 Apollo 安装向导失败: " + ex.Message);
            }
        }

        public ActionResult ControlApollo(string action)
        {
            try
            {
                RuntimeSnapshot s = new RuntimeSnapshot();
                InspectApollo(s);
                if (string.IsNullOrEmpty(s.ApolloServiceName))
                {
                    if (action == "stop" || action == "restart")
                    {
                        bool killed = KillProcessesByNames(new string[] { "apollo", "sunshine" });
                        if (action == "stop")
                        {
                            return killed
                                ? new ActionResult(true, "已停止运行中的 Apollo/Sunshine 进程。")
                                : new ActionResult(false, "未检测到正在运行的 Apollo 服务或进程。");
                        }
                    }
                    if ((action == "start" || action == "restart") && !string.IsNullOrEmpty(s.ApolloExePath) && File.Exists(s.ApolloExePath))
                    {
                        Process.Start(s.ApolloExePath);
                        return new ActionResult(true, "已直接启动 Apollo 可执行程序。");
                    }
                    if (!string.IsNullOrEmpty(s.ApolloInstallerPath) && File.Exists(s.ApolloInstallerPath))
                    {
                        return new ActionResult(false, "Apollo 尚未安装，请先点击【打开安装向导】完成安装。");
                    }
                    return new ActionResult(false, "未检测到 Apollo 串流服务端，请先完成安装。");
                }

                if (!IsAdministrator())
                {
                    return new ActionResult(false, "控制 Windows 系统服务需要管理员权限。");
                }

                using (ServiceController sc = new ServiceController(s.ApolloServiceName))
                {
                    if (action == "stop" || action == "restart")
                    {
                        if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(12));
                        }
                    }
                    if (action == "start" || action == "restart")
                    {
                        sc.Refresh();
                        if (sc.Status != ServiceControllerStatus.Running)
                        {
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                        }
                    }
                }
                string label = action == "start" ? "启动" : (action == "stop" ? "停止" : "重启");
                return new ActionResult(true, string.Format("Apollo 服务 ({0}) 已成功{1}。", s.ApolloServiceName, label));
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "操作 Apollo 服务失败: " + ex.Message);
            }
        }

        private static bool IsProcessRunning(string name)
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(name);
                try { return ps.Length > 0; }
                finally
                {
                    for (int i = 0; i < ps.Length; i++) { try { ps[i].Dispose(); } catch { } }
                }
            }
            catch { return false; }
        }

        private static bool KillProcessesByNames(string[] names)
        {
            bool any = false;
            for (int k = 0; k < names.Length; k++)
            {
                try
                {
                    Process[] ps = Process.GetProcessesByName(names[k]);
                    try
                    {
                        for (int i = 0; i < ps.Length; i++)
                        {
                            try { ps[i].Kill(); ps[i].WaitForExit(3000); any = true; } catch { }
                        }
                    }
                    finally
                    {
                        for (int i = 0; i < ps.Length; i++) { try { ps[i].Dispose(); } catch { } }
                    }
                }
                catch { }
            }
            return any;
        }

        public ActionResult SetEasyTierAutostart(bool enable)
        {
            if (!IsAdministrator()) return new ActionResult(false, "管理开机自启计划任务需要管理员权限。");
            try
            {
                if (!enable)
                {
                    int c;
                    RunProcessCaptureWithCode("schtasks.exe", "/delete /tn \"EasyTier-NoTun-Autostart\" /f", WorkspaceRoot, 4000, out c);
                    RunProcessCaptureWithCode("schtasks.exe", "/delete /tn \"EasyTier-RemoteControl\" /f", WorkspaceRoot, 4000, out c);
                    return new ActionResult(true, "已关闭 EasyTier 开机自启。");
                }
                string xml = BuildScheduledTaskXml(
                    "S-1-5-18",
                    "ServiceAccount",
                    true,
                    true,
                    false,
                    "IgnoreNew",
                    CoreExePath,
                    string.Format("-c \"{0}\" --console-log-level warn", ConfigTomlPath),
                    EasyTierDir);
                string err;
                if (RegisterTaskFromXml("EasyTier-NoTun-Autostart", xml, out err))
                {
                    return new ActionResult(true, "已开启 EasyTier 系统级开机自启。");
                }
                return new ActionResult(false, "注册计划任务失败: " + err);
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "配置 EasyTier 自启异常: " + ex.Message);
            }
        }

        public ActionResult SetUiAutostart(bool enable, bool startMinimized)
        {
            if (!IsAdministrator()) return new ActionResult(false, "管理开机自启计划任务需要管理员权限。");
            try
            {
                if (!enable)
                {
                    int c;
                    RunProcessCaptureWithCode("schtasks.exe", "/delete /tn \"RemoteControlUI-Autostart\" /f", WorkspaceRoot, 4000, out c);
                    return new ActionResult(true, "已关闭控制台开机自启。");
                }
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string userSid = WindowsIdentity.GetCurrent().User.Value;
                string xml = BuildScheduledTaskXml(
                    userSid,
                    "InteractiveToken",
                    false,
                    true,
                    true,
                    "IgnoreNew",
                    exePath,
                    startMinimized ? "--minimized" : "",
                    Path.GetDirectoryName(exePath));
                string err;
                if (RegisterTaskFromXml("RemoteControlUI-Autostart", xml, out err))
                {
                    return new ActionResult(true, "已开启控制台开机静默自启。");
                }
                return new ActionResult(false, "注册控制台自启任务失败: " + err);
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "配置控制台自启异常: " + ex.Message);
            }
        }

        public ActionResult RegisterSilentElevatedLauncher()
        {
            if (!IsAdministrator()) return new ActionResult(false, "注册免弹窗启动任务需要管理员权限。");
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string exeDir = Path.GetDirectoryName(exePath);
                string launcherPath = Path.Combine(exeDir, "RemoteControlLauncher.exe");
                string shortcutTarget = File.Exists(launcherPath) ? launcherPath : exePath;
                string userSid = WindowsIdentity.GetCurrent().User.Value;

                string xml = BuildScheduledTaskXml(
                    userSid,
                    "InteractiveToken",
                    false,
                    false,
                    false,
                    "Parallel",
                    exePath,
                    "",
                    exeDir);
                string err;
                if (!RegisterTaskFromXml("RemoteControlUI-ElevatedLaunch", xml, out err))
                {
                    return new ActionResult(false, "注册免弹窗任务失败: " + err);
                }

                string sddlErr;
                bool granted = GrantTaskExecutePermission("RemoteControlUI-ElevatedLaunch", userSid, out sddlErr);
                if (CheckTaskExists("RemoteControlUI-Autostart", true))
                {
                    string ignoredErr;
                    GrantTaskExecutePermission("RemoteControlUI-Autostart", userSid, out ignoredErr);
                }
                CreateDesktopShortcut("远程控制中心.lnk", shortcutTarget, "", exeDir, exePath + ",0", "远程控制中心");
                if (!granted)
                {
                    return new ActionResult(false, "计划任务已创建，但写入普通用户免弹窗权限失败: " + sddlErr);
                }
                return new ActionResult(true, "免弹窗启动任务及桌面快捷方式已更新。");
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "异常: " + ex.Message);
            }
        }

        public static string BuildScheduledTaskXml(
            string principalUserId,
            string logonType,
            bool bootTrigger,
            bool logonTrigger,
            bool bindLogonTriggerToUser,
            string multipleInstancesPolicy,
            string command,
            string arguments,
            string workingDirectory)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            sb.AppendLine("  <Triggers>");
            if (bootTrigger)
            {
                sb.AppendLine("    <BootTrigger><Enabled>true</Enabled></BootTrigger>");
            }
            if (logonTrigger)
            {
                sb.AppendLine("    <LogonTrigger>");
                sb.AppendLine("      <Enabled>true</Enabled>");
                if (bindLogonTriggerToUser && !string.IsNullOrEmpty(principalUserId))
                {
                    sb.AppendFormat("      <UserId>{0}</UserId>\r\n", SecurityElement.Escape(principalUserId));
                }
                sb.AppendLine("    </LogonTrigger>");
            }
            sb.AppendLine("  </Triggers>");
            sb.AppendLine("  <Principals>");
            sb.AppendLine("    <Principal id=\"Author\">");
            sb.AppendFormat("      <UserId>{0}</UserId>\r\n", SecurityElement.Escape(principalUserId));
            if (!string.IsNullOrEmpty(logonType))
            {
                sb.AppendFormat("      <LogonType>{0}</LogonType>\r\n", SecurityElement.Escape(logonType));
            }
            sb.AppendLine("      <RunLevel>HighestAvailable</RunLevel>");
            sb.AppendLine("    </Principal>");
            sb.AppendLine("  </Principals>");
            sb.AppendLine("  <Settings>");
            sb.AppendFormat("    <MultipleInstancesPolicy>{0}</MultipleInstancesPolicy>\r\n", SecurityElement.Escape(multipleInstancesPolicy));
            sb.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            sb.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
            sb.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            sb.AppendLine("    <Enabled>true</Enabled>");
            sb.AppendLine("    <Hidden>false</Hidden>");
            sb.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            if (bootTrigger)
            {
                sb.AppendLine("    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>");
            }
            sb.AppendLine("  </Settings>");
            sb.AppendLine("  <Actions Context=\"Author\">");
            sb.AppendLine("    <Exec>");
            sb.AppendFormat("      <Command>{0}</Command>\r\n", SecurityElement.Escape(command));
            if (!string.IsNullOrEmpty(arguments))
            {
                sb.AppendFormat("      <Arguments>{0}</Arguments>\r\n", SecurityElement.Escape(arguments));
            }
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                sb.AppendFormat("      <WorkingDirectory>{0}</WorkingDirectory>\r\n", SecurityElement.Escape(workingDirectory));
            }
            sb.AppendLine("    </Exec>");
            sb.AppendLine("  </Actions>");
            sb.AppendLine("</Task>");
            return sb.ToString();
        }

        private bool RegisterTaskFromXml(string taskName, string xmlContent, out string errorMsg)
        {
            errorMsg = "";
            string tmpXml = Path.Combine(Path.GetTempPath(), "rc_task_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(tmpXml, xmlContent, Encoding.Unicode);
                int code;
                string outStr = RunProcessCaptureWithCode(
                    "schtasks.exe",
                    string.Format("/create /tn \"{0}\" /xml \"{1}\" /f", taskName, tmpXml),
                    WorkspaceRoot,
                    6000,
                    out code);
                if (code == 0) return true;
                errorMsg = outStr;
                return false;
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(tmpXml)) File.Delete(tmpXml); } catch { }
            }
        }

        public static bool HasTaskExecuteAce(string sddl, string userSid)
        {
            if (string.IsNullOrEmpty(sddl) || string.IsNullOrEmpty(userSid)) return false;
            // Windows canonicalizes FRFX into 0x1200a9 (FILE_GENERIC_READ | FILE_GENERIC_EXECUTE)
            string pattern = "\\(A;;[^;]*(?:GX|FX|FA|GA|0x1200a9|0x1f01ff)[^;]*;;;" + Regex.Escape(userSid) + "\\)";
            return Regex.IsMatch(sddl, pattern, RegexOptions.IgnoreCase);
        }

        private bool GrantTaskExecutePermission(string taskName, string userSid, out string errorMsg)
        {
            errorMsg = "";
            try
            {
                Type schedType = Type.GetTypeFromProgID("Schedule.Service");
                if (schedType == null)
                {
                    errorMsg = "无法加载 Schedule.Service COM 组件";
                    return false;
                }
                object svc = Activator.CreateInstance(schedType);
                schedType.InvokeMember("Connect", BindingFlags.InvokeMethod, null, svc, null);
                object folder = schedType.InvokeMember("GetFolder", BindingFlags.InvokeMethod, null, svc, new object[] { "\\" });
                object task = folder.GetType().InvokeMember("GetTask", BindingFlags.InvokeMethod, null, folder, new object[] { taskName });
                string sddl = task.GetType().InvokeMember("GetSecurityDescriptor", BindingFlags.InvokeMethod, null, task, new object[] { 4 }) as string;
                if (string.IsNullOrEmpty(sddl))
                {
                    errorMsg = "读取任务安全描述符为空";
                    return false;
                }
                if (!HasTaskExecuteAce(sddl, userSid))
                {
                    string ace = "(A;;FRFX;;;" + userSid + ")";
                    int sIdx = sddl.IndexOf("S:", StringComparison.Ordinal);
                    string newSddl = sIdx >= 0 ? (sddl.Substring(0, sIdx) + ace + sddl.Substring(sIdx)) : (sddl + ace);
                    // SetSecurityDescriptor takes flags = 0 (NOT 4!)
                    task.GetType().InvokeMember("SetSecurityDescriptor", BindingFlags.InvokeMethod, null, task, new object[] { newSddl, 0 });
                }
                string taskFile = Path.Combine(@"C:\Windows\System32\Tasks", taskName);
                if (File.Exists(taskFile))
                {
                    int c;
                    RunProcessCaptureWithCode("icacls.exe", string.Format("\"{0}\" /grant \"*{1}:(RX)\" /C", taskFile, userSid), WorkspaceRoot, 3000, out c);
                }
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                return false;
            }
        }

        private static void CreateDesktopShortcut(string shortcutFileName, string targetPath, string arguments, string workDir, string iconLocation, string description)
        {
            try
            {
                Type wshType = Type.GetTypeFromProgID("WScript.Shell");
                if (wshType == null) return;
                object wsh = Activator.CreateInstance(wshType);
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnkPath = Path.Combine(desktop, shortcutFileName);
                object lnk = wshType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, wsh, new object[] { lnkPath });
                Type lnkType = lnk.GetType();
                lnkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { targetPath });
                lnkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { arguments ?? "" });
                lnkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir ?? "" });
                lnkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { iconLocation ?? "" });
                lnkType.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { description ?? "" });
                lnkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
        }

        public ActionResult InitializeFreshConfig()
        {
            try
            {
                EasyTierConfig cfg = new EasyTierConfig();
                cfg.Ipv4 = "10.126.126.1/24";
                cfg.NetworkName = "rc-" + GenerateRandomString(8, "abcdefghijklmnopqrstuvwxyz0123456789");
                cfg.NetworkSecret = GenerateRandomString(20, "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
                cfg.Peers = new List<string>
                {
                    "tcp://225284.xyz:11010",
                    "tcp://easytier.weiai.org.cn:11010",
                    "tcp://183.230.36.171:11010"
                };
                cfg.NoTun = true;
                cfg.Save(ConfigTomlPath);
                return new ActionResult(true, "已现场生成全新随机组网配置 (no_tun = true)。");
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "初始化配置失败: " + ex.Message);
            }
        }

        public ActionResult RegenerateCredentials()
        {
            try
            {
                EasyTierConfig cfg = LoadConfig();
                string oldName = cfg.NetworkName;
                string oldSecret = cfg.NetworkSecret;
                cfg.NetworkName = "rc-" + GenerateRandomString(8, "abcdefghijklmnopqrstuvwxyz0123456789");
                cfg.NetworkSecret = GenerateRandomString(20, "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
                cfg.NoTun = true;
                cfg.Save(ConfigTomlPath);

                // Update pairing manual text files (workspace + Desktop if present)
                string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string[] docs = new string[]
                {
                    Path.Combine(WorkspaceRoot, "我的配对信息与避坑手册.txt"),
                    Path.Combine(WorkspaceRoot, "我的远程配对信息.txt"),
                    Path.Combine(desktopDir, "我的远程配对信息.txt")
                };
                for (int i = 0; i < docs.Length; i++)
                {
                    try
                    {
                        if (File.Exists(docs[i]) && !string.IsNullOrEmpty(oldName) && !string.IsNullOrEmpty(oldSecret))
                        {
                            string txt = File.ReadAllText(docs[i], Encoding.UTF8);
                            txt = txt.Replace(oldName, cfg.NetworkName).Replace(oldSecret, cfg.NetworkSecret);
                            File.WriteAllText(docs[i], txt, new UTF8Encoding(true));
                        }
                    }
                    catch { }
                }

                if (File.Exists(CoreExePath))
                {
                    ActionResult restartRes = RestartEasyTier();
                    if (!restartRes.Ok)
                    {
                        return new ActionResult(false, string.Format("新密钥 ({0}) 已写入配置，但重启 EasyTier 失败：{1}", cfg.NetworkName, restartRes.Message));
                    }
                }
                return new ActionResult(true, string.Format("已生成全新专属密钥 ({0}) 并自动重启 EasyTier！请同步更新平板端。", cfg.NetworkName));
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "重新生成密钥失败: " + ex.Message);
            }
        }

        public ActionResult SavePeersAndRestart(List<string> newPeers)
        {
            try
            {
                if (newPeers == null || newPeers.Count == 0)
                {
                    return new ActionResult(false, "请至少保留 1 个有效的公共节点地址。");
                }
                EasyTierConfig cfg = LoadConfig();
                cfg.Peers = new List<string>(newPeers);
                cfg.NoTun = true;
                cfg.Save(ConfigTomlPath);
                if (File.Exists(CoreExePath))
                {
                    return RestartEasyTier();
                }
                return new ActionResult(true, "节点列表已保存至 config.toml。");
            }
            catch (Exception ex)
            {
                return new ActionResult(false, "保存节点配置失败: " + ex.Message);
            }
        }

        public static Task<NodeSpeedResult> TestNodeLatencyAsync(string uri, bool enabledInConfig)
        {
            return Task.Run<NodeSpeedResult>(async () =>
            {
                NodeSpeedResult res = new NodeSpeedResult
                {
                    Uri = uri,
                    EnabledInConfig = enabledInConfig,
                    Reachable = false,
                    LatencyMs = -1,
                    StatusText = "测速失败"
                };

                try
                {
                    string host;
                    int port;
                    string scheme;
                    if (!ParsePeerUri(uri, out scheme, out host, out port))
                    {
                        res.StatusText = "格式无效";
                        return res;
                    }

                    Stopwatch sw = Stopwatch.StartNew();
                    using (TcpClient client = new TcpClient())
                    {
                        Task connectTask = client.ConnectAsync(host, port);
                        // Observe any faulted continuation to prevent UnobservedTaskException
                        Task _ = connectTask.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        Task completed = await Task.WhenAny(connectTask, Task.Delay(2500)).ConfigureAwait(false);
                        if (completed == connectTask)
                        {
                            if (connectTask.IsFaulted || !client.Connected)
                            {
                                res.StatusText = scheme == "udp" ? "UDP同口未应答" : "不可达";
                            }
                            else
                            {
                                sw.Stop();
                                res.Reachable = true;
                                long rawMs = sw.ElapsedMilliseconds;
                                res.LatencyMs = (int)Math.Max(1, rawMs);
                                bool suspectProxyTun = (rawMs <= 2 && !IsPrivateOrLoopbackHost(host));
                                if (scheme == "udp")
                                {
                                    res.StatusText = string.Format("{0} ms (TCP探测)", res.LatencyMs);
                                }
                                else if (suspectProxyTun)
                                {
                                    res.StatusText = string.Format("{0} ms (代理接管?)", res.LatencyMs);
                                }
                                else
                                {
                                    res.StatusText = string.Format("{0} ms", res.LatencyMs);
                                }
                            }
                        }
                        else
                        {
                            res.StatusText = scheme == "udp" ? "UDP/超时" : "连接超时";
                        }
                    }
                }
                catch
                {
                    res.StatusText = "不可达";
                }
                return res;
            });
        }

        public static bool ParsePeerUri(string uri, out string scheme, out string host, out int port)
        {
            scheme = "tcp";
            host = "";
            port = 11010;
            if (string.IsNullOrWhiteSpace(uri)) return false;
            try
            {
                Uri parsed = new Uri(uri.Trim());
                scheme = parsed.Scheme.ToLowerInvariant();
                host = parsed.Host;
                port = parsed.Port > 0 ? parsed.Port : 11010;
                return !string.IsNullOrEmpty(host);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPrivateOrLoopbackHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            string h = host.Trim().Trim('[', ']').ToLowerInvariant();
            if (h == "localhost" || h == "::1") return true;
            IPAddress ip;
            if (IPAddress.TryParse(h, out ip))
            {
                if (IPAddress.IsLoopback(ip)) return true;
                byte[] b = ip.GetAddressBytes();
                if (b.Length == 4)
                {
                    if (b[0] == 10 || b[0] == 127) return true;
                    if (b[0] == 192 && b[1] == 168) return true;
                    if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                }
            }
            return false;
        }

        public static string GenerateRandomString(int length, string chars)
        {
            if (length <= 0 || string.IsNullOrEmpty(chars)) return "";
            int n = chars.Length;
            int limit = 256 - (256 % n);
            StringBuilder sb = new StringBuilder(length);
            byte[] buf = new byte[Math.Max(32, length * 2)];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                while (sb.Length < length)
                {
                    rng.GetBytes(buf);
                    for (int i = 0; i < buf.Length && sb.Length < length; i++)
                    {
                        if (buf[i] < limit)
                        {
                            sb.Append(chars[buf[i] % n]);
                        }
                    }
                }
            }
            return sb.ToString();
        }

        public static string BuildPairingSummaryText(EasyTierConfig cfg)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RemoteControl Pairing Info (Apollo + EasyTier)");
            sb.AppendLine("Host IP: " + cfg.HostIpOnly);
            sb.AppendLine("Tablet IP: " + cfg.SuggestedTabletIp + cfg.CidrPrefix);
            sb.AppendLine("Network Name: " + (string.IsNullOrEmpty(cfg.NetworkName) ? "(未配置)" : cfg.NetworkName));
            sb.AppendLine("Network Secret: " + (string.IsNullOrEmpty(cfg.NetworkSecret) ? "(未配置)" : cfg.NetworkSecret));
            sb.AppendLine("Peer: " + cfg.PrimaryPeer);
            sb.AppendLine("Apollo Web: https://localhost:47990");
            return sb.ToString().TrimEnd();
        }

        public static string BuildPairingJsonText(EasyTierConfig cfg)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["host_ip"] = cfg.HostIpOnly;
            payload["tablet_ip"] = cfg.SuggestedTabletIp + cfg.CidrPrefix;
            payload["network_name"] = cfg.NetworkName ?? "";
            payload["network_secret"] = cfg.NetworkSecret ?? "";
            payload["peer"] = cfg.PrimaryPeer;
            payload["no_tun_host"] = true;
            JavaScriptSerializer ser = new JavaScriptSerializer();
            return ser.Serialize(payload);
        }

        public static string BuildProxyYamlRules(EasyTierConfig cfg)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("rules:");
            sb.AppendLine("  - PROCESS-NAME,easytier-core.exe,DIRECT");
            sb.AppendLine("  - IP-CIDR," + cfg.SubnetCidr + ",DIRECT,no-resolve");
            sb.AppendLine("  - PROCESS-NAME,apollo.exe,DIRECT");
            sb.AppendLine("  - PROCESS-NAME,sunshine.exe,DIRECT");
            return sb.ToString().TrimEnd();
        }

        private static string GetDictStr(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return "";
            return dict[key].ToString();
        }

        private static string RunProcessCapture(string fileName, string arguments, string workDir, int timeoutMs, Encoding encoding = null)
        {
            int code;
            return RunProcessCaptureWithCode(fileName, arguments, workDir, timeoutMs, out code, encoding);
        }

        private static string RunProcessCaptureWithCode(string fileName, string arguments, string workDir, int timeoutMs, out int exitCode, Encoding encoding = null)
        {
            exitCode = -1;
            Encoding enc = encoding ?? Encoding.Default;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = arguments;
                psi.WorkingDirectory = Directory.Exists(workDir) ? workDir : Environment.CurrentDirectory;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = enc;
                psi.StandardErrorEncoding = enc;

                using (Process p = Process.Start(psi))
                {
                    if (p == null) return "";
                    StringBuilder outSb = new StringBuilder();
                    StringBuilder errSb = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) outSb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) errSb.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (p.WaitForExit(timeoutMs))
                    {
                        p.WaitForExit();
                        exitCode = p.ExitCode;
                        string o = outSb.ToString().Trim();
                        return string.IsNullOrEmpty(o) ? errSb.ToString().Trim() : o;
                    }
                    else
                    {
                        try { p.Kill(); } catch { }
                        return "TIMEOUT";
                    }
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
