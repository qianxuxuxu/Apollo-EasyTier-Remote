using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace RemoteControlUI
{
    public class EasyTierConfig
    {
        public string ConfigPath { get; set; }
        public bool Exists { get; set; }
        public string Ipv4 { get; set; }
        public bool Dhcp { get; set; }
        public List<string> Listeners { get; set; }
        public string NetworkName { get; set; }
        public string NetworkSecret { get; set; }
        public List<string> Peers { get; set; }
        public bool NoTun { get; set; }
        public bool EnableEncryption { get; set; }
        public bool EnableIpv6 { get; set; }

        public EasyTierConfig()
        {
            Ipv4 = "10.126.126.1/24";
            Dhcp = false;
            Listeners = new List<string> { "tcp://0.0.0.0:11010", "udp://0.0.0.0:11010" };
            NetworkName = "";
            NetworkSecret = "";
            Peers = new List<string>();
            NoTun = true;
            EnableEncryption = true;
            EnableIpv6 = true;
        }

        public string HostIpOnly
        {
            get
            {
                if (string.IsNullOrEmpty(Ipv4)) return "10.126.126.1";
                int slash = Ipv4.IndexOf('/');
                return slash > 0 ? Ipv4.Substring(0, slash) : Ipv4;
            }
        }

        public string CidrPrefix
        {
            get
            {
                if (!string.IsNullOrEmpty(Ipv4))
                {
                    int slash = Ipv4.IndexOf('/');
                    if (slash > 0 && slash < Ipv4.Length - 1)
                    {
                        return Ipv4.Substring(slash);
                    }
                }
                return "/24";
            }
        }

        public string SubnetCidr
        {
            get
            {
                string ip = HostIpOnly;
                string[] parts = ip.Split('.');
                if (parts.Length == 4)
                {
                    return string.Format("{0}.{1}.{2}.0{3}", parts[0], parts[1], parts[2], CidrPrefix);
                }
                return "10.126.126.0/24";
            }
        }

        public string SuggestedTabletIp
        {
            get
            {
                string ip = HostIpOnly;
                string[] parts = ip.Split('.');
                if (parts.Length == 4)
                {
                    int last;
                    int nextOctet = 2;
                    if (int.TryParse(parts[3], out last) && last == 2)
                    {
                        nextOctet = 3;
                    }
                    return string.Format("{0}.{1}.{2}.{3}", parts[0], parts[1], parts[2], nextOctet);
                }
                return "10.126.126.2";
            }
        }

        public string PrimaryPeer
        {
            get
            {
                if (Peers != null && Peers.Count > 0) return Peers[0];
                return "tcp://225284.xyz:11010";
            }
        }

        public static EasyTierConfig Load(string path)
        {
            EasyTierConfig cfg = new EasyTierConfig();
            cfg.ConfigPath = path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                cfg.Exists = false;
                cfg.Peers = new List<string>
                {
                    "tcp://225284.xyz:11010",
                    "tcp://easytier.weiai.org.cn:11010",
                    "tcp://183.230.36.171:11010"
                };
                return cfg;
            }

            cfg.Exists = true;
            string content = File.ReadAllText(path, Encoding.UTF8);
            string section = "";
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            List<string> parsedPeers = new List<string>();
            List<string> parsedListeners = new List<string>();
            bool inMultilineListeners = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                // Remove inline comments outside quotes (single or double)
                int hashIdx = FindUnquotedChar(line, '#');
                if (hashIdx >= 0) line = line.Substring(0, hashIdx).Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (inMultilineListeners)
                {
                    MatchCollection mc = Regex.Matches(line, "\"((?:\\\\.|[^\"\\\\])*)\"");
                    foreach (Match m in mc)
                    {
                        parsedListeners.Add(UnescapeToml(m.Groups[1].Value));
                    }
                    if (line.Contains("]")) inMultilineListeners = false;
                    continue;
                }

                if (line.StartsWith("[[") && line.EndsWith("]]"))
                {
                    section = line.Substring(2, line.Length - 4).Trim().ToLowerInvariant();
                    continue;
                }
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string rawVal = line.Substring(eq + 1).Trim();

                if (section == "" && key == "ipv4")
                {
                    cfg.Ipv4 = Unquote(rawVal);
                }
                else if (section == "" && key == "dhcp")
                {
                    cfg.Dhcp = ParseBool(rawVal, false);
                }
                else if (section == "" && key == "listeners")
                {
                    MatchCollection mc = Regex.Matches(rawVal, "\"((?:\\\\.|[^\"\\\\])*)\"");
                    foreach (Match m in mc)
                    {
                        parsedListeners.Add(UnescapeToml(m.Groups[1].Value));
                    }
                    if (rawVal.Contains("[") && !rawVal.Contains("]"))
                    {
                        inMultilineListeners = true;
                    }
                }
                else if (section == "network_identity" && key == "network_name")
                {
                    cfg.NetworkName = Unquote(rawVal);
                }
                else if (section == "network_identity" && key == "network_secret")
                {
                    cfg.NetworkSecret = Unquote(rawVal);
                }
                else if (section == "peer" && key == "uri")
                {
                    string u = Unquote(rawVal);
                    if (!string.IsNullOrEmpty(u) && !parsedPeers.Contains(u))
                    {
                        parsedPeers.Add(u);
                    }
                }
                else if (section == "flags" && key == "no_tun")
                {
                    cfg.NoTun = ParseBool(rawVal, true);
                }
                else if (section == "flags" && key == "enable_encryption")
                {
                    cfg.EnableEncryption = ParseBool(rawVal, true);
                }
                else if (section == "flags" && key == "enable_ipv6")
                {
                    cfg.EnableIpv6 = ParseBool(rawVal, true);
                }
            }

            if (parsedListeners.Count > 0) cfg.Listeners = parsedListeners;
            if (parsedPeers.Count > 0) cfg.Peers = parsedPeers;
            else
            {
                cfg.Peers = new List<string>
                {
                    "tcp://225284.xyz:11010",
                    "tcp://easytier.weiai.org.cn:11010",
                    "tcp://183.230.36.171:11010"
                };
            }
            return cfg;
        }

        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path)) path = ConfigPath;
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("配置文件路径为空");

            // Hard safety redline: always enforce no_tun = true
            NoTun = true;
            if (Listeners == null || Listeners.Count == 0)
            {
                Listeners = new List<string> { "tcp://0.0.0.0:11010", "udp://0.0.0.0:11010" };
            }
            if (Peers == null || Peers.Count == 0)
            {
                Peers = new List<string> { "tcp://225284.xyz:11010", "tcp://easytier.weiai.org.cn:11010", "tcp://183.230.36.171:11010" };
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("ipv4 = \"{0}\"\r\n", EscapeToml(string.IsNullOrEmpty(Ipv4) ? "10.126.126.1/24" : Ipv4));
            sb.AppendFormat("dhcp = {0}\r\n", Dhcp ? "true" : "false");

            List<string> quotedListeners = new List<string>();
            for (int i = 0; i < Listeners.Count; i++)
            {
                quotedListeners.Add("\"" + EscapeToml(Listeners[i]) + "\"");
            }
            sb.AppendFormat("listeners = [{0}]\r\n\r\n", string.Join(", ", quotedListeners.ToArray()));

            sb.AppendLine("[network_identity]");
            sb.AppendFormat("network_name = \"{0}\"\r\n", EscapeToml(NetworkName ?? ""));
            sb.AppendFormat("network_secret = \"{0}\"\r\n\r\n", EscapeToml(NetworkSecret ?? ""));

            for (int i = 0; i < Peers.Count; i++)
            {
                string peerUri = (Peers[i] ?? "").Trim();
                if (string.IsNullOrEmpty(peerUri)) continue;
                sb.AppendLine("[[peer]]");
                sb.AppendFormat("uri = \"{0}\"\r\n\r\n", EscapeToml(peerUri));
            }

            sb.AppendLine("[flags]");
            sb.AppendLine("no_tun = true");
            sb.AppendFormat("enable_encryption = {0}\r\n", EnableEncryption ? "true" : "false");
            sb.AppendFormat("enable_ipv6 = {0}\r\n", EnableIpv6 ? "true" : "false");

            // EasyTier TOML requires UTF-8 without BOM
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            ConfigPath = path;
            Exists = true;
        }

        private static int FindUnquotedChar(string s, char ch)
        {
            bool inDouble = false;
            bool inSingle = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' && !inSingle)
                {
                    // Count preceding backslashes to handle escaped backslashes properly
                    int bs = 0;
                    for (int k = i - 1; k >= 0 && s[k] == '\\'; k--) bs++;
                    if (bs % 2 == 0) inDouble = !inDouble;
                }
                else if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                }
                if (!inDouble && !inSingle && c == ch) return i;
            }
            return -1;
        }

        private static string Unquote(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                return UnescapeToml(s.Substring(1, s.Length - 2));
            }
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'')
            {
                return s.Substring(1, s.Length - 2);
            }
            return s;
        }

        private static string UnescapeToml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    if (next == '\\' || next == '"')
                    {
                        sb.Append(next);
                        i++;
                        continue;
                    }
                    if (next == 'n') { sb.Append('\n'); i++; continue; }
                    if (next == 'r') { sb.Append('\r'); i++; continue; }
                    if (next == 't') { sb.Append('\t'); i++; continue; }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static bool ParseBool(string s, bool fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            string low = s.Trim().ToLowerInvariant();
            if (low == "true") return true;
            if (low == "false") return false;
            return fallback;
        }

        private static string EscapeToml(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    public class UiSettings
    {
        public string Theme { get; set; }
        public string AppTitle { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public bool StartMinimizedAutostart { get; set; }
        public int RefreshIntervalSeconds { get; set; }

        public UiSettings()
        {
            Theme = "Light";
            AppTitle = "远程控制中心";
            MinimizeToTrayOnClose = true;
            StartMinimizedAutostart = true;
            RefreshIntervalSeconds = 5;
        }

        public static UiSettings Load(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    UiSettings loaded = ser.Deserialize<UiSettings>(json);
                    if (loaded != null)
                    {
                        if (string.IsNullOrEmpty(loaded.Theme)) loaded.Theme = "Light";
                        if (string.IsNullOrEmpty(loaded.AppTitle)) loaded.AppTitle = "远程控制中心";
                        if (loaded.RefreshIntervalSeconds < 2 || loaded.RefreshIntervalSeconds > 60)
                        {
                            loaded.RefreshIntervalSeconds = 5;
                        }
                        return loaded;
                    }
                }
            }
            catch { }
            return new UiSettings();
        }

        public void Save(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                JavaScriptSerializer ser = new JavaScriptSerializer();
                string json = ser.Serialize(this);
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch { }
        }
    }

    public class PeerNodeInfo
    {
        public string Hostname { get; set; }
        public string VirtualIp { get; set; }
        public string CostMode { get; set; }
        public string LatencyText { get; set; }
        public double LatencyMs { get; set; }
        public string LossRate { get; set; }
        public string TunnelProto { get; set; }
        public string Version { get; set; }
        public bool IsLocal { get; set; }
        public bool IsPublicServer { get; set; }
    }

    public class FirewallAuditResult
    {
        public int PassedCount { get; set; }
        public int TotalCount { get; set; }
        public List<string> MissingRules { get; set; }
        public List<string> PresentRules { get; set; }
        public bool AllOk { get { return TotalCount > 0 && PassedCount >= TotalCount; } }

        public FirewallAuditResult()
        {
            TotalCount = 5;
            MissingRules = new List<string>();
            PresentRules = new List<string>();
        }
    }

    public class DisplayAdapterInfo
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public string PnpId { get; set; }
        public string Category { get; set; } // "Physical", "ApolloVDA", "ThirdPartyVirtual"
    }

    public class NodeSpeedResult
    {
        public string Uri { get; set; }
        public string Label { get; set; }
        public bool Reachable { get; set; }
        public int LatencyMs { get; set; }
        public string StatusText { get; set; }
        public bool EnabledInConfig { get; set; }
    }

    public class RuntimeSnapshot
    {
        public DateTime Timestamp { get; set; }
        public bool IsAdmin { get; set; }
        public string WorkspaceRoot { get; set; }
        public EasyTierConfig Config { get; set; }
        public bool EasyTierInstalled { get; set; }
        public bool EasyTierRunning { get; set; }
        public string EasyTierPids { get; set; }
        public string EasyTierVersion { get; set; }
        public string EasyTierNodeIp { get; set; }
        public string EasyTierHostname { get; set; }
        public bool ApolloInstalled { get; set; }
        public bool ApolloRunning { get; set; }
        public bool ApolloListening47990 { get; set; }
        public string ApolloServiceName { get; set; }
        public string ApolloExePath { get; set; }
        public string ApolloInstallerPath { get; set; }
        public List<PeerNodeInfo> Peers { get; set; }
        public string SummaryMode { get; set; } // "P2P", "Relay", "LocalOnly", "Offline"
        public string SummaryLatency { get; set; }
        public bool NoTunConfigOk { get; set; }
        public bool NoTunAdapterOk { get; set; }
        public List<string> BadVirtualAdapters { get; set; }
        public FirewallAuditResult Firewall { get; set; }
        public bool EasyTierAutostart { get; set; }
        public bool UiAutostart { get; set; }
        public bool ElevatedTaskReady { get; set; }
        public bool AcNeverSleep { get; set; }
        public List<DisplayAdapterInfo> DisplayAdapters { get; set; }

        public RuntimeSnapshot()
        {
            Timestamp = DateTime.Now;
            Config = new EasyTierConfig();
            Peers = new List<PeerNodeInfo>();
            BadVirtualAdapters = new List<string>();
            Firewall = new FirewallAuditResult();
            DisplayAdapters = new List<DisplayAdapterInfo>();
            SummaryMode = "Offline";
            SummaryLatency = "--";
        }
    }
}
