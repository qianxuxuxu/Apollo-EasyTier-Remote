using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RemoteControlUI
{
    public partial class MainWindow
    {
        // =========================================================================
        // PAGE 0: 全部设备 (UU Remote inspired clean device overview)
        // =========================================================================
        private UIElement BuildHomePage()
        {
            StackPanel page = new StackPanel();
            EasyTierConfig cfg = _snapshot.Config;

            // Top Heading Row
            DockPanel headingRow = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
            StackPanel headRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            StackPanel modePill = UiFactory.CreateStatusInline(GetModeShortText(_snapshot), GetModeTone(_snapshot));
            modePill.Margin = new Thickness(0, 0, 14, 0);
            headRight.Children.Add(modePill);
            Button copyAllBtn = UiFactory.CreateButton("复制连接信息", "secondary", (s, e) =>
            {
                CopyText(BackendService.BuildPairingSummaryText(cfg), "连接信息");
            }, 28);
            headRight.Children.Add(copyAllBtn);
            DockPanel.SetDock(headRight, Dock.Right);
            headingRow.Children.Add(headRight);
            headingRow.Children.Add(UiFactory.CreatePageHeading("全部设备"));
            page.Children.Add(headingRow);

            // Partition peers into: remote user devices vs public relay servers
            List<PeerNodeInfo> remoteDevices = new List<PeerNodeInfo>();
            List<PeerNodeInfo> publicRelays = new List<PeerNodeInfo>();
            if (_snapshot.Peers != null)
            {
                for (int i = 0; i < _snapshot.Peers.Count; i++)
                {
                    PeerNodeInfo p = _snapshot.Peers[i];
                    if (p.IsLocal) continue;
                    if (p.IsPublicServer) publicRelays.Add(p);
                    else remoteDevices.Add(p);
                }
            }

            // -----------------------------------------------------------------
            // Section 1: 电脑 1 (Local Host Device Card — UU Remote Signature)
            // -----------------------------------------------------------------
            page.Children.Add(UiFactory.CreateGroupHeader("电脑 1", _secHostExpanded, () =>
            {
                _secHostExpanded = !_secHostExpanded;
                RenderPageContent(0, true);
            }));

            if (_secHostExpanded)
            {
            StackPanel hostCardBody = new StackPanel();
            DockPanel hostRow = new DockPanel();

            // Right actions on host card
            StackPanel hostRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button copyIpBtn = UiFactory.CreateButton("复制 IP", "secondary", (s, e) => CopyText(cfg.HostIpOnly, "本机 IP"), 28);
            copyIpBtn.Margin = new Thickness(0, 0, 6, 0);
            Button qrBtn = UiFactory.CreateButton("配对码", "secondary", (s, e) => SelectPage(2), 28);
            qrBtn.Margin = new Thickness(0, 0, 6, 0);
            Button infoBtn = UiFactory.CreateIconButton(UiIcons.InfoCircle, "查看本机详情", (s, e) =>
            {
                _showHostDetail = !_showHostDetail;
                RenderPageContent(0, true);
            }, 28, 16);
            hostRight.Children.Add(copyIpBtn);
            hostRight.Children.Add(qrBtn);
            hostRight.Children.Add(infoBtn);
            DockPanel.SetDock(hostRight, Dock.Right);
            hostRow.Children.Add(hostRight);

            // Left icon + host identity
            StackPanel hostLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            UIElement winBadge = UiIcons.CreateWindowsSquareBadge(36);
            ((FrameworkElement)winBadge).Margin = new Thickness(0, 0, 12, 0);
            hostLeft.Children.Add(winBadge);

            StackPanel hostInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            StackPanel hostNameLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string hostName = !string.IsNullOrEmpty(_snapshot.EasyTierHostname) ? _snapshot.EasyTierHostname : Environment.MachineName;
            TextBlock hostNameTxt = new TextBlock
            {
                Text = hostName,
                FontFamily = UiFactory.SansFont,
                FontSize = 14.2,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            hostNameTxt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            hostNameLine.Children.Add(hostNameTxt);

            Border localTag = UiFactory.CreateTag("本机", "accent");
            localTag.Margin = new Thickness(8, 0, 0, 0);
            hostNameLine.Children.Add(localTag);

            TextBlock ipTxt = new TextBlock
            {
                Text = cfg.HostIpOnly,
                FontFamily = UiFactory.MonoFont,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 1, 0, 0)
            };
            ipTxt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            hostNameLine.Children.Add(ipTxt);
            hostInfo.Children.Add(hostNameLine);

            // Concise inline service summary below hostname
            StackPanel subStatus = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            StackPanel etInline = UiFactory.CreateStatusInline(
                _snapshot.EasyTierRunning ? "EasyTier 运行中" : "EasyTier 未启动",
                _snapshot.EasyTierRunning ? "success" : "danger");
            etInline.Margin = new Thickness(0, 0, 14, 0);
            subStatus.Children.Add(etInline);

            string apShort = _snapshot.ApolloListening47990 ? "Apollo 运行中" : (_snapshot.ApolloRunning ? "Apollo 运行中" : (_snapshot.ApolloInstalled ? "Apollo 已停止" : "Apollo 未安装"));
            string apTone = (_snapshot.ApolloListening47990 || _snapshot.ApolloRunning) ? "success" : (_snapshot.ApolloInstalled ? "warning" : "danger");
            StackPanel apInline = UiFactory.CreateStatusInline(apShort, apTone);
            subStatus.Children.Add(apInline);

            if (!_snapshot.NoTunConfigOk || !_snapshot.NoTunAdapterOk)
            {
                Border ntWarn = UiFactory.CreateTag("检查 no_tun", "danger");
                ntWarn.Margin = new Thickness(10, 0, 0, 0);
                subStatus.Children.Add(ntWarn);
            }
            hostInfo.Children.Add(subStatus);
            hostLeft.Children.Add(hostInfo);
            hostRow.Children.Add(hostLeft);
            hostCardBody.Children.Add(hostRow);

            // Collapsible detail strip toggled by (i) button
            if (_showHostDetail)
            {
                hostCardBody.Children.Add(UiFactory.CreateDivider(new Thickness(0, 12, 0, 10)));
                DockPanel detDock = new DockPanel();
                Button openApDetBtn = UiFactory.CreateButton("打开 Apollo 后台", "link", (s, e) => OpenApolloWeb(), 26);
                DockPanel.SetDock(openApDetBtn, Dock.Right);
                detDock.Children.Add(openApDetBtn);

                string detText = string.Format(
                    "虚拟子网: {0}   |   EasyTier: {1}{2}   |   Apollo: {3}   |   no_tun: {4}",
                    cfg.Ipv4,
                    string.IsNullOrEmpty(_snapshot.EasyTierVersion) ? "已就绪" : _snapshot.EasyTierVersion,
                    string.IsNullOrEmpty(_snapshot.EasyTierPids) ? "" : (" (PID " + _snapshot.EasyTierPids + ")"),
                    string.IsNullOrEmpty(_snapshot.ApolloServiceName) ? (string.IsNullOrEmpty(_snapshot.ApolloExePath) ? "未检测到" : _snapshot.ApolloExePath) : (_snapshot.ApolloServiceName + " :47990"),
                    (_snapshot.NoTunConfigOk && _snapshot.NoTunAdapterOk) ? "正常 (0 虚拟网卡)" : "异常");
                TextBlock detTb = UiFactory.CreateSubtle(detText, 11.8, true);
                detTb.VerticalAlignment = VerticalAlignment.Center;
                detDock.Children.Add(detTb);
                hostCardBody.Children.Add(detDock);
            }

            page.Children.Add(UiFactory.CreateCard(hostCardBody, new Thickness(16, 13, 14, 13), new Thickness(0, 0, 0, 18)));
            }

            // -----------------------------------------------------------------
            // Section 2: 手机 / 平板 N (Connected Peer Devices)
            // -----------------------------------------------------------------
            page.Children.Add(UiFactory.CreateGroupHeader(string.Format("手机 / 平板 {0}", remoteDevices.Count), _secRemoteExpanded, () =>
            {
                _secRemoteExpanded = !_secRemoteExpanded;
                RenderPageContent(0, true);
            }));

            if (_secRemoteExpanded)
            {
            if (remoteDevices.Count == 0)
            {
                DockPanel emptyRow = new DockPanel();
                Button pairBtn = UiFactory.CreateButton("查看配对方式", "link", (s, e) => SelectPage(2), 28);
                DockPanel.SetDock(pairBtn, Dock.Right);
                emptyRow.Children.Add(pairBtn);

                StackPanel emptyLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Border mutedIcon = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 12, 0),
                    Child = UiIcons.Create(UiIcons.Tablet, 18, new SolidColorBrush(ThemeManager.TextMuted), 1.8)
                };
                mutedIcon.SetResourceReference(Border.BackgroundProperty, "Brush.SubtleBg");
                emptyLeft.Children.Add(mutedIcon);

                StackPanel emptyTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                TextBlock eTitle = new TextBlock
                {
                    Text = _snapshot.EasyTierRunning ? "暂无在线的平板或远程设备" : "EasyTier 未启动",
                    FontSize = 13.5,
                    FontFamily = UiFactory.SansFont
                };
                eTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
                emptyTxt.Children.Add(eTitle);
                emptyTxt.Children.Add(UiFactory.CreateSubtle(
                    _snapshot.EasyTierRunning ? ("在平板 EasyTier 启动连接后，设备 (" + cfg.SuggestedTabletIp + ") 会自动显示在这里") : "先在下方启动 EasyTier 服务", 11.8));
                emptyLeft.Children.Add(emptyTxt);
                emptyRow.Children.Add(emptyLeft);

                page.Children.Add(UiFactory.CreateCard(emptyRow, new Thickness(16, 13, 14, 13), new Thickness(0, 0, 0, 8)));
            }
            else
            {
                for (int i = 0; i < remoteDevices.Count; i++)
                {
                    PeerNodeInfo p = remoteDevices[i];
                    page.Children.Add(BuildRemoteDeviceCard(p));
                }
            }
            }

            // Optional collapsible public relay servers summary (keeps device list clean!)
            if (publicRelays.Count > 0)
            {
                UIElement relayToggle = UiFactory.CreateGroupHeader(
                    string.Format("公共中继节点 {0}", publicRelays.Count),
                    _showPublicRelays,
                    () => { _showPublicRelays = !_showPublicRelays; RenderPageContent(0, true); });
                ((FrameworkElement)relayToggle).Margin = new Thickness(0, 6, 0, 6);
                page.Children.Add(relayToggle);

                if (_showPublicRelays)
                {
                    StackPanel relayList = new StackPanel();
                    for (int i = 0; i < publicRelays.Count; i++)
                    {
                        PeerNodeInfo r = publicRelays[i];
                        DockPanel rr = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                        string rMeta = string.Format("{0}   ·   丢包 {1}   ·   {2}",
                            string.IsNullOrEmpty(r.LatencyText) ? "-" : r.LatencyText,
                            string.IsNullOrEmpty(r.LossRate) ? "0.0%" : r.LossRate,
                            string.IsNullOrEmpty(r.TunnelProto) ? "tcp" : r.TunnelProto);
                        TextBlock rRight = UiFactory.CreateSubtle(rMeta, 11.8, false);
                        rRight.FontFamily = UiFactory.MonoFont;
                        DockPanel.SetDock(rRight, Dock.Right);
                        rr.Children.Add(rRight);

                        TextBlock rName = new TextBlock
                        {
                            Text = string.IsNullOrEmpty(r.Hostname) ? "公共节点" : r.Hostname,
                            FontSize = 12.2,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Margin = new Thickness(0, 0, 12, 0)
                        };
                        rName.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
                        rr.Children.Add(rName);
                        relayList.Children.Add(rr);
                        if (i < publicRelays.Count - 1) relayList.Children.Add(UiFactory.CreateDivider(new Thickness(0, 2, 0, 2)));
                    }
                    page.Children.Add(UiFactory.CreateCard(relayList, new Thickness(14, 8, 14, 8), new Thickness(0, 0, 0, 10)));
                }
            }

            // -----------------------------------------------------------------
            // Section 3: 服务与凭据 (Compact Clean Controls)
            // -----------------------------------------------------------------
            UIElement svcHeader = UiFactory.CreateGroupHeader("服务与凭据", _secServicesExpanded, () =>
            {
                _secServicesExpanded = !_secServicesExpanded;
                RenderPageContent(0, true);
            });
            ((FrameworkElement)svcHeader).Margin = new Thickness(0, 12, 0, 8);
            page.Children.Add(svcHeader);

            if (_secServicesExpanded)
            {
            // 2-Column Service Control Cards
            Grid svcGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            svcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            svcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            svcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // EasyTier Compact Card
            StackPanel etPanel = new StackPanel();
            DockPanel etTop = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            StackPanel etStat = UiFactory.CreateStatusInline(
                _snapshot.EasyTierRunning ? "运行中" : (_snapshot.EasyTierInstalled ? "已停止" : "未安装"),
                _snapshot.EasyTierRunning ? "success" : "danger");
            DockPanel.SetDock(etStat, Dock.Right);
            etTop.Children.Add(etStat);
            etTop.Children.Add(UiFactory.CreateTitle("EasyTier 组网", 13.5));
            etPanel.Children.Add(etTop);

            StackPanel etBtns = new StackPanel { Orientation = Orientation.Horizontal };
            Button etStart = UiFactory.CreateButton("启动", _snapshot.EasyTierRunning ? "secondary" : "primary", (s, e) => RunBusyAction("正在启动 EasyTier...", () => _backend.StartEasyTier(), false), 28, 56);
            etStart.IsEnabled = !_snapshot.EasyTierRunning;
            etStart.Margin = new Thickness(0, 0, 6, 0);
            Button etStop = UiFactory.CreateButton("停止", "danger", (s, e) => RunBusyAction("正在停止 EasyTier...", () => _backend.StopEasyTier(), false), 28, 56);
            etStop.IsEnabled = _snapshot.EasyTierRunning;
            etStop.Margin = new Thickness(0, 0, 6, 0);
            Button etRestart = UiFactory.CreateButton("重启", "secondary", (s, e) => RunBusyAction("正在重启 EasyTier...", () => _backend.RestartEasyTier(), false), 28, 56);
            etBtns.Children.Add(etStart);
            etBtns.Children.Add(etStop);
            etBtns.Children.Add(etRestart);
            etPanel.Children.Add(etBtns);

            Border etCard = UiFactory.CreateCard(etPanel, new Thickness(15, 12, 15, 12), new Thickness(0));
            Grid.SetColumn(etCard, 0);
            svcGrid.Children.Add(etCard);

            // Apollo Compact Card
            StackPanel apPanel = new StackPanel();
            DockPanel apTop = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            string apStateTxt = _snapshot.ApolloListening47990 ? "运行中 · 47990" : (_snapshot.ApolloRunning ? "运行中" : (_snapshot.ApolloInstalled ? "已停止" : "未安装"));
            string apStateTone = (_snapshot.ApolloListening47990 || _snapshot.ApolloRunning) ? "success" : (_snapshot.ApolloInstalled ? "warning" : "danger");
            StackPanel apStat = UiFactory.CreateStatusInline(apStateTxt, apStateTone);
            DockPanel.SetDock(apStat, Dock.Right);
            apTop.Children.Add(apStat);
            apTop.Children.Add(UiFactory.CreateTitle("Apollo 串流", 13.5));
            apPanel.Children.Add(apTop);

            StackPanel apBtns = new StackPanel { Orientation = Orientation.Horizontal };
            if (!_snapshot.ApolloInstalled && !string.IsNullOrEmpty(_snapshot.ApolloInstallerPath))
            {
                Button apInstall = UiFactory.CreateButton("打开安装向导", "primary", (s, e) =>
                {
                    RunBusyAction("正在打开 Apollo 安装向导...", () => _backend.InstallApolloSilent(), true);
                }, 28);
                apInstall.Margin = new Thickness(0, 0, 6, 0);
                apBtns.Children.Add(apInstall);
            }
            else
            {
                Button apStart = UiFactory.CreateButton("启动", _snapshot.ApolloRunning ? "secondary" : "primary", (s, e) => RunBusyAction("正在启动 Apollo...", () => _backend.ControlApollo("start"), false), 28, 52);
                apStart.IsEnabled = _snapshot.ApolloInstalled && !_snapshot.ApolloRunning;
                apStart.Margin = new Thickness(0, 0, 6, 0);
                Button apStop = UiFactory.CreateButton("停止", "danger", (s, e) => RunBusyAction("正在停止 Apollo...", () => _backend.ControlApollo("stop"), false), 28, 52);
                apStop.IsEnabled = _snapshot.ApolloInstalled && _snapshot.ApolloRunning;
                apStop.Margin = new Thickness(0, 0, 6, 0);
                Button apRestart = UiFactory.CreateButton("重启", "secondary", (s, e) => RunBusyAction("正在重启 Apollo...", () => _backend.ControlApollo("restart"), false), 28, 52);
                apRestart.IsEnabled = _snapshot.ApolloInstalled;
                apRestart.Margin = new Thickness(0, 0, 6, 0);
                apBtns.Children.Add(apStart);
                apBtns.Children.Add(apStop);
                apBtns.Children.Add(apRestart);
            }
            Button apWeb = UiFactory.CreateButton("后台", "link", (s, e) => OpenApolloWeb(), 28);
            apBtns.Children.Add(apWeb);
            apPanel.Children.Add(apBtns);

            Border apCard = UiFactory.CreateCard(apPanel, new Thickness(15, 12, 15, 12), new Thickness(0));
            Grid.SetColumn(apCard, 2);
            svcGrid.Children.Add(apCard);

            page.Children.Add(svcGrid);

            // Compact Credentials Card
            StackPanel credBox = new StackPanel();
            DockPanel credTop = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            if (!cfg.Exists)
            {
                Button initBtn = UiFactory.CreateButton("生成随机配置", "primary", (s, e) =>
                {
                    RunBusyAction("正在生成随机配置...", () => _backend.InitializeFreshConfig(), true);
                }, 26);
                DockPanel.SetDock(initBtn, Dock.Right);
                credTop.Children.Add(initBtn);
            }
            else
            {
                Button regenBtn = UiFactory.CreateButton("重置密钥", "danger", (s, e) =>
                {
                    MessageBoxResult r = MessageBox.Show(
                        "确定要重新生成网络名称和密钥吗？\n\n重置后会自动重启 EasyTier，平板端需同步修改为新密钥。",
                        "重置组网密钥", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (r == MessageBoxResult.Yes)
                    {
                        RunBusyAction("正在重置密钥并重启 EasyTier...", () => _backend.RegenerateCredentials(), false);
                    }
                }, 26);
                DockPanel.SetDock(regenBtn, Dock.Right);
                credTop.Children.Add(regenBtn);
            }
            credTop.Children.Add(UiFactory.CreateTitle("组网凭据", 13.5));
            credBox.Children.Add(credTop);

            Grid credCols = new Grid();
            credCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            credCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            credCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.38, GridUnitType.Star) });

            UIElement nameCol = CreateCompactCredentialCell(
                "网络名称",
                string.IsNullOrEmpty(cfg.NetworkName) ? "未配置" : (_showNetworkName ? cfg.NetworkName : "••••••••••"),
                _showNetworkName,
                () => { _showNetworkName = !_showNetworkName; RenderPageContent(0, true); },
                () => CopyText(cfg.NetworkName, "网络名称"),
                new Thickness(0, 0, 14, 0));
            Grid.SetColumn(nameCol, 0);
            credCols.Children.Add(nameCol);

            Border vDiv = new Border { Width = 1, Margin = new Thickness(0, 2, 0, 2) };
            vDiv.SetResourceReference(Border.BackgroundProperty, "Brush.Divider");
            Grid.SetColumn(vDiv, 1);
            credCols.Children.Add(vDiv);

            UIElement secCol = CreateCompactCredentialCell(
                "网络密钥",
                string.IsNullOrEmpty(cfg.NetworkSecret) ? "未配置" : (_showNetworkSecret ? cfg.NetworkSecret : "••••••••••••••••"),
                _showNetworkSecret,
                () => { _showNetworkSecret = !_showNetworkSecret; RenderPageContent(0, true); },
                () => CopyText(cfg.NetworkSecret, "网络密钥"),
                new Thickness(14, 0, 0, 0));
            Grid.SetColumn(secCol, 2);
            credCols.Children.Add(secCol);

            credBox.Children.Add(credCols);
            page.Children.Add(UiFactory.CreateCard(credBox, new Thickness(15, 12, 15, 12), new Thickness(0)));
            }

            return page;
        }

        private Border BuildRemoteDeviceCard(PeerNodeInfo p)
        {
            StackPanel cardBody = new StackPanel();
            DockPanel row = new DockPanel();
            string cleanIp = (p.VirtualIp ?? "").Split('/')[0];
            string peerKey = !string.IsNullOrEmpty(cleanIp) ? cleanIp : (p.Hostname ?? "");
            bool isExpanded = string.Equals(_expandedRemotePeerKey, peerKey, StringComparison.OrdinalIgnoreCase);

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(cleanIp) && cleanIp != "-")
            {
                Button cpBtn = UiFactory.CreateButton("复制 IP", "ghost", (s, e) => CopyText(cleanIp, "设备 IP"), 27);
                cpBtn.Margin = new Thickness(0, 0, 4, 0);
                right.Children.Add(cpBtn);
            }
            Button peerInfoBtn = UiFactory.CreateIconButton(UiIcons.InfoCircle, "查看设备详情", (s, e) =>
            {
                _expandedRemotePeerKey = isExpanded ? null : peerKey;
                RenderPageContent(0, true);
            }, 28, 16);
            right.Children.Add(peerInfoBtn);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            UIElement badge = UiIcons.CreateTabletSquareBadge(36);
            ((FrameworkElement)badge).Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(badge);

            StackPanel info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            StackPanel nameLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            string rawName = (p.Hostname ?? "").Trim();
            string displayName = (string.IsNullOrEmpty(rawName) || string.Equals(rawName, "localhost", StringComparison.OrdinalIgnoreCase))
                ? "平板 / 移动设备"
                : rawName;

            TextBlock nameTxt = new TextBlock
            {
                Text = displayName,
                FontFamily = UiFactory.SansFont,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            nameTxt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            nameLine.Children.Add(nameTxt);

            if (!string.IsNullOrEmpty(cleanIp) && cleanIp != "-")
            {
                TextBlock ipTxt = new TextBlock
                {
                    Text = cleanIp,
                    FontFamily = UiFactory.MonoFont,
                    FontSize = 12.2,
                    Margin = new Thickness(10, 1, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                ipTxt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                nameLine.Children.Add(ipTxt);
            }
            info.Children.Add(nameLine);

            string lowCost = (p.CostMode ?? "").ToLowerInvariant();
            bool isP2p = lowCost.Contains("p2p") || lowCost == "direct" || lowCost == "1";
            string connLabel = isP2p ? "P2P 直连" : (lowCost.Contains("relay") ? "中继连接" : (string.IsNullOrEmpty(p.CostMode) ? "在线" : p.CostMode));
            string connTone = isP2p ? "success" : "warning";
            string subMeta = connLabel;
            if (!string.IsNullOrEmpty(p.LatencyText) && p.LatencyText != "-") subMeta += " · " + p.LatencyText;
            if (!string.IsNullOrEmpty(p.LossRate) && p.LossRate != "-") subMeta += " · 丢包 " + p.LossRate;
            if (!string.IsNullOrEmpty(p.TunnelProto) && p.TunnelProto != "-") subMeta += " · " + p.TunnelProto;

            StackPanel statLine = UiFactory.CreateStatusInline(subMeta, connTone);
            statLine.Margin = new Thickness(0, 4, 0, 0);
            info.Children.Add(statLine);

            left.Children.Add(info);
            row.Children.Add(left);
            cardBody.Children.Add(row);

            if (isExpanded)
            {
                cardBody.Children.Add(UiFactory.CreateDivider(new Thickness(0, 10, 0, 8)));
                string detailStr = string.Format(
                    "虚拟地址: {0}   |   连接方式: {1}   |   隧道协议: {2}   |   客户端版本: {3}",
                    string.IsNullOrEmpty(p.VirtualIp) ? "-" : p.VirtualIp,
                    connLabel,
                    string.IsNullOrEmpty(p.TunnelProto) ? "-" : p.TunnelProto,
                    string.IsNullOrEmpty(p.Version) ? "-" : p.Version);
                cardBody.Children.Add(UiFactory.CreateSubtle(detailStr, 11.6, true));
            }
            return UiFactory.CreateCard(cardBody, new Thickness(16, 13, 14, 13), new Thickness(0, 0, 0, 8));
        }

        private UIElement CreateCompactCredentialCell(string label, string displayValue, bool isVisible, Action onToggle, Action onCopy, Thickness margin)
        {
            DockPanel dp = new DockPanel { Margin = margin };
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button vis = UiFactory.CreateButton(isVisible ? "隐藏" : "显示", "ghost", (s, e) => onToggle(), 25);
            vis.Margin = new Thickness(0, 0, 2, 0);
            Button cp = UiFactory.CreateButton("复制", "secondary", (s, e) => onCopy(), 25);
            btns.Children.Add(vis);
            btns.Children.Add(cp);
            DockPanel.SetDock(btns, Dock.Right);
            dp.Children.Add(btns);

            StackPanel left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(UiFactory.CreateSubtle(label, 11.5, false));
            TextBlock val = new TextBlock
            {
                Text = displayValue,
                FontFamily = UiFactory.MonoFont,
                FontSize = 12.6,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            val.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            left.Children.Add(val);
            dp.Children.Add(left);
            return dp;
        }

        // =========================================================================
        // PAGE 1: 网络与服务 (Public Nodes, no_tun, Firewall, Autostart, Displays)
        // =========================================================================
        private UIElement BuildNetworkPage()
        {
            StackPanel page = new StackPanel();
            EasyTierConfig cfg = _snapshot.Config;

            DockPanel head = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
            Button speedBtn = UiFactory.CreateButton(_isTestingNodes ? "测速中..." : "节点测速", "primary", (s, e) => RunNodeSpeedTests(), 30);
            speedBtn.IsEnabled = !_isTestingNodes;
            DockPanel.SetDock(speedBtn, Dock.Right);
            head.Children.Add(speedBtn);
            head.Children.Add(UiFactory.CreatePageHeading("网络与服务"));
            page.Children.Add(head);

            // 1. Public Peer Nodes Card (clean hairline rows, zero nested card clutter)
            StackPanel nodesCard = new StackPanel();
            DockPanel nHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            Button monitorLink = UiFactory.CreateButton("查看可用节点监控", "link", (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://info.qtet.cn/uptime/easytier") { UseShellExecute = true }); }
                catch { CopyText("https://info.qtet.cn/uptime/easytier", "节点监控网址"); }
            }, 26);
            DockPanel.SetDock(monitorLink, Dock.Right);
            nHead.Children.Add(monitorLink);
            nHead.Children.Add(UiFactory.CreateTitle("公共节点", 14));
            nodesCard.Children.Add(nHead);

            List<string> allNodes = new List<string>();
            for (int i = 0; i < cfg.Peers.Count; i++)
            {
                if (!allNodes.Contains(cfg.Peers[i])) allNodes.Add(cfg.Peers[i]);
            }
            for (int i = 0; i < BackendService.BuiltinPublicNodes.Length; i++)
            {
                if (!allNodes.Contains(BackendService.BuiltinPublicNodes[i])) allNodes.Add(BackendService.BuiltinPublicNodes[i]);
            }

            for (int i = 0; i < allNodes.Count; i++)
            {
                string nodeUri = allNodes[i];
                bool enabled = cfg.Peers.Contains(nodeUri);
                bool isPrimary = enabled && cfg.Peers.Count > 0 && string.Equals(cfg.Peers[0], nodeUri, StringComparison.OrdinalIgnoreCase);

                DockPanel rd = new DockPanel { Margin = new Thickness(0, 7, 0, 7) };
                StackPanel rActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                NodeSpeedResult spd;
                string latLabel = "未测速";
                string latTone = "neutral";
                if (_nodeSpeedCache.TryGetValue(nodeUri, out spd))
                {
                    latLabel = spd.StatusText;
                    if (spd.Reachable) latTone = spd.LatencyMs <= 80 ? "success" : "warning";
                    else latTone = "danger";
                }
                Border latTag = UiFactory.CreateTag(latLabel, latTone);
                latTag.Margin = new Thickness(0, 0, 10, 0);
                rActions.Children.Add(latTag);

                if (!isPrimary)
                {
                    string capturedUri = nodeUri;
                    Button primaryBtn = UiFactory.CreateButton("设为首选", "secondary", (s, e) =>
                    {
                        List<string> updated = new List<string>(cfg.Peers);
                        updated.RemoveAll(x => string.Equals(x, capturedUri, StringComparison.OrdinalIgnoreCase));
                        updated.Insert(0, capturedUri);
                        RunBusyAction("正在切换主节点并重启 EasyTier...", () => _backend.SavePeersAndRestart(updated), false);
                    }, 26);
                    primaryBtn.Margin = new Thickness(0, 0, 6, 0);
                    rActions.Children.Add(primaryBtn);
                }

                string capToggleUri = nodeUri;
                Button toggleNodeBtn = UiFactory.CreateButton(enabled ? "移除" : "启用", enabled ? "ghost" : "link", (s, e) =>
                {
                    List<string> updated = new List<string>(cfg.Peers);
                    if (enabled)
                    {
                        if (updated.Count <= 1)
                        {
                            ShowToast("至少保留 1 个节点", "error");
                            return;
                        }
                        updated.RemoveAll(x => string.Equals(x, capToggleUri, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        if (!updated.Contains(capToggleUri)) updated.Add(capToggleUri);
                    }
                    RunBusyAction("正在保存节点并重启 EasyTier...", () => _backend.SavePeersAndRestart(updated), false);
                }, 26);
                rActions.Children.Add(toggleNodeBtn);

                DockPanel.SetDock(rActions, Dock.Right);
                rd.Children.Add(rActions);

                StackPanel rLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                TextBlock uriTxt = new TextBlock
                {
                    Text = nodeUri,
                    FontFamily = UiFactory.MonoFont,
                    FontSize = 12.8,
                    FontWeight = enabled ? FontWeights.Medium : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                uriTxt.SetResourceReference(TextBlock.ForegroundProperty, enabled ? "Brush.TextPrimary" : "Brush.TextMuted");
                rLeft.Children.Add(uriTxt);

                if (isPrimary)
                {
                    Border pTag = UiFactory.CreateTag("主节点", "accent");
                    pTag.Margin = new Thickness(8, 0, 0, 0);
                    rLeft.Children.Add(pTag);
                }
                else if (enabled)
                {
                    Border bTag = UiFactory.CreateTag("备用", "success");
                    bTag.Margin = new Thickness(8, 0, 0, 0);
                    rLeft.Children.Add(bTag);
                }

                rd.Children.Add(rLeft);
                nodesCard.Children.Add(rd);
                if (i < allNodes.Count - 1) nodesCard.Children.Add(UiFactory.CreateDivider());
            }

            // Custom Node Input Row
            DockPanel addNodeDock = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            TextBox customNodeInput = null;
            Action submitCustomNode = () =>
            {
                string val = (customNodeInput != null ? (customNodeInput.Text ?? "") : "").Trim();
                string sc, h; int pt;
                if (!BackendService.ParsePeerUri(val, out sc, out h, out pt) || (sc != "tcp" && sc != "udp" && sc != "wg" && sc != "ws" && sc != "wss"))
                {
                    ShowToast("节点地址格式无效，示例: tcp://1.2.3.4:11010", "error");
                    return;
                }
                _customNodeDraft = "";
                List<string> updated = new List<string>(cfg.Peers);
                if (!updated.Contains(val)) updated.Add(val);
                RunBusyAction("正在添加节点并重启 EasyTier...", () => _backend.SavePeersAndRestart(updated), false);
            };
            UIElement customNodeBox = UiFactory.CreatePlaceholderTextBox(
                _customNodeDraft,
                "输入自定义节点，如 tcp://example.com:11010",
                val => { _customNodeDraft = val; },
                submitCustomNode,
                out customNodeInput,
                32);
            Button addNodeBtn = UiFactory.CreateButton("添加节点", "secondary", (s, e) => submitCustomNode(), 32);
            addNodeBtn.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(addNodeBtn, Dock.Right);
            addNodeDock.Children.Add(addNodeBtn);
            addNodeDock.Children.Add(customNodeBox);
            nodesCard.Children.Add(addNodeDock);

            TextBlock tunTip = UiFactory.CreateSubtle("注：开启代理 TUN 时测速若显示 1~2 ms 可能为本地代理接管，请以全部设备中的实际连接延迟为准。", 11.2);
            tunTip.Margin = new Thickness(0, 8, 0, 0);
            nodesCard.Children.Add(tunTip);

            page.Children.Add(UiFactory.CreateCard(nodesCard, new Thickness(16, 14, 16, 14), new Thickness(0, 0, 0, 14)));

            // 2. Bottom 2-Column Grid: Left (Health + Displays) | Right (Autostart & Power)
            Grid sysGrid = new Grid();
            sysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            sysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left Column
            StackPanel leftCol = new StackPanel();

            // Health Checks Card (no_tun + Firewall combined cleanly)
            StackPanel checkStack = new StackPanel();
            checkStack.Children.Add(UiFactory.CreateTitle("网络与防火墙", 14));

            // Row 1: no_tun
            DockPanel ntRow = new DockPanel { Margin = new Thickness(0, 12, 0, 10) };
            bool ntSafe = _snapshot.NoTunConfigOk && _snapshot.NoTunAdapterOk;
            StackPanel ntRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ntRight.Children.Add(UiFactory.CreateTag(ntSafe ? "0 冲突网卡" : "异常", ntSafe ? "success" : "danger"));
            if (!ntSafe)
            {
                Button fixNtBtn = UiFactory.CreateButton("修复", "danger", (s, e) =>
                {
                    RunBusyAction("正在写入 no_tun = true 并重启 EasyTier...", () => _backend.RepairNoTunConfig(), true);
                }, 26);
                fixNtBtn.Margin = new Thickness(6, 0, 0, 0);
                ntRight.Children.Add(fixNtBtn);
            }
            DockPanel.SetDock(ntRight, Dock.Right);
            ntRow.Children.Add(ntRight);

            StackPanel ntLeft = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            ntLeft.Children.Add(UiFactory.CreateTitle("无虚拟网卡模式 (no_tun)", 13));
            string ntSub = ntSafe
                ? "不创建虚拟网卡，与代理软件 TUN 共存"
                : (!_snapshot.NoTunConfigOk
                    ? "config.toml 中 no_tun 未开启"
                    : ("发现 EasyTier 虚拟网卡: " + string.Join(", ", _snapshot.BadVirtualAdapters.ToArray())));
            ntLeft.Children.Add(UiFactory.CreateSubtle(ntSub, 11.5));
            ntRow.Children.Add(ntLeft);
            checkStack.Children.Add(ntRow);

            checkStack.Children.Add(UiFactory.CreateDivider());

            // Row 2: Firewall
            FirewallAuditResult fw = _snapshot.Firewall;
            DockPanel fwRow = new DockPanel { Margin = new Thickness(0, 10, 0, 2) };
            StackPanel fwRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Border fwTag = UiFactory.CreateTag(string.Format("{0}/{1} 已放行", fw.PassedCount, fw.TotalCount), fw.AllOk ? "success" : "warning");
            fwTag.Margin = new Thickness(0, 0, 8, 0);
            fwRight.Children.Add(fwTag);
            Button fwFixBtn = UiFactory.CreateButton(fw.AllOk ? "重新检查" : "一键修复", fw.AllOk ? "secondary" : "primary", (s, e) =>
            {
                if (fw.AllOk)
                {
                    ShowToast("正在重新检查防火墙规则...", "info");
                    RefreshSnapshotAsync(true, false);
                }
                else
                {
                    RunBusyAction("正在修复防火墙规则...", () => _backend.RepairFirewallRules(), true);
                }
            }, 26);
            fwRight.Children.Add(fwFixBtn);
            DockPanel.SetDock(fwRight, Dock.Right);
            fwRow.Children.Add(fwRight);

            StackPanel fwLeft = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            fwLeft.Children.Add(UiFactory.CreateTitle("Windows 防火墙", 13));
            fwLeft.Children.Add(UiFactory.CreateSubtle(
                fw.AllOk ? "串流与组网端口已放行" : ("缺失: " + string.Join(", ", fw.MissingRules.ToArray())), 11.5));
            fwRow.Children.Add(fwLeft);
            checkStack.Children.Add(fwRow);

            leftCol.Children.Add(UiFactory.CreateCard(checkStack, new Thickness(16, 14, 16, 14), new Thickness(0, 0, 0, 12)));

            // Display Adapters Card
            StackPanel dispStack = new StackPanel();
            DockPanel dHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            Button devMgmtBtn = UiFactory.CreateButton("设备管理器", "link", (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }); }
                catch (Exception ex) { ShowToast("打开失败: " + ex.Message, "error"); }
            }, 26);
            DockPanel.SetDock(devMgmtBtn, Dock.Right);
            dHead.Children.Add(devMgmtBtn);
            dHead.Children.Add(UiFactory.CreateTitle("显卡与虚拟屏", 14));
            dispStack.Children.Add(dHead);

            int thirdPartyCount = 0;
            for (int i = 0; i < _snapshot.DisplayAdapters.Count; i++)
            {
                DisplayAdapterInfo da = _snapshot.DisplayAdapters[i];
                if (da.Category == "ThirdPartyVirtual") thirdPartyCount++;
                DockPanel dr = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                string tagTxt = da.Category == "ApolloVDA" ? "Apollo" : (da.Category == "ThirdPartyVirtual" ? "第三方" : "物理显卡");
                string tagTone = da.Category == "ApolloVDA" ? "success" : (da.Category == "ThirdPartyVirtual" ? "warning" : "neutral");
                Border db = UiFactory.CreateTag(tagTxt, tagTone);
                DockPanel.SetDock(db, Dock.Right);
                dr.Children.Add(db);

                TextBlock dn = new TextBlock
                {
                    Text = da.Name,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                dn.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
                dr.Children.Add(dn);
                dispStack.Children.Add(dr);
            }
            if (thirdPartyCount > 0)
            {
                TextBlock note = UiFactory.CreateSubtle("已有第三方虚拟屏均保留未改动；如遇黑屏或光标卡假屏，可在设备管理器临时禁用。", 11.2);
                note.Margin = new Thickness(0, 6, 0, 0);
                dispStack.Children.Add(note);
            }
            leftCol.Children.Add(UiFactory.CreateCard(dispStack, new Thickness(16, 14, 16, 14), new Thickness(0)));

            Grid.SetColumn(leftCol, 0);
            sysGrid.Children.Add(leftCol);

            // Right Column: Autostart & Power
            StackPanel rightCol = new StackPanel();
            StackPanel togStack = new StackPanel();
            togStack.Children.Add(UiFactory.CreateTitle("开机与电源", 14));

            togStack.Children.Add(CreateSettingToggleRow(
                "EasyTier 开机自启",
                "系统后台静默启动，不弹黑框",
                _snapshot.EasyTierAutostart,
                val => RunBusyAction("正在更新 EasyTier 自启...", () => _backend.SetEasyTierAutostart(val), true),
                true));

            togStack.Children.Add(CreateSettingToggleRow(
                "控制台开机自启",
                "登录后自动最小化到托盘",
                _snapshot.UiAutostart,
                val => RunBusyAction("正在更新控制台自启...", () => _backend.SetUiAutostart(val, true), true,
                    res =>
                    {
                        if (res.Ok)
                        {
                            _settings.StartMinimizedAutostart = val;
                            _settings.Save(_backend.SettingsPath);
                        }
                    }),
                true));

            togStack.Children.Add(CreateSettingToggleRow(
                "接通电源时不休眠",
                "防止闲置休眠导致远程断连",
                _snapshot.AcNeverSleep,
                val => RunBusyAction("正在更新电源策略...", () => _backend.SetAcNeverSleep(val), true),
                false));

            Border lidHint = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 10, 0, 0)
            };
            lidHint.SetResourceReference(Border.BackgroundProperty, "Brush.SubtleBg");
            lidHint.Child = UiFactory.CreateSubtle("笔记本合盖使用：请在 Windows 电源选项中将「关闭盖子时（接通电源）」设为「不采取任何操作」。", 11.4);
            togStack.Children.Add(lidHint);

            rightCol.Children.Add(UiFactory.CreateCard(togStack, new Thickness(16, 14, 16, 14), new Thickness(0)));
            Grid.SetColumn(rightCol, 2);
            sysGrid.Children.Add(rightCol);

            page.Children.Add(sysGrid);
            return page;
        }

        private void RunNodeSpeedTests()
        {
            if (_isTestingNodes) return;
            _isTestingNodes = true;
            RenderPageContent(1, true);
            ShowToast("正在测速公共节点...", "info");

            List<string> targets = new List<string>(_snapshot.Config.Peers);
            for (int i = 0; i < BackendService.BuiltinPublicNodes.Length; i++)
            {
                if (!targets.Contains(BackendService.BuiltinPublicNodes[i])) targets.Add(BackendService.BuiltinPublicNodes[i]);
            }

            Task.Run(() =>
            {
                List<Task<NodeSpeedResult>> tasks = new List<Task<NodeSpeedResult>>();
                for (int i = 0; i < targets.Count; i++)
                {
                    string u = targets[i];
                    tasks.Add(BackendService.TestNodeLatencyAsync(u, _snapshot.Config.Peers.Contains(u)));
                }
                Task.WaitAll(tasks.ToArray());

                Dispatcher.BeginInvoke((Action)(() =>
                {
                    for (int i = 0; i < tasks.Count; i++)
                    {
                        NodeSpeedResult r = tasks[i].Result;
                        _nodeSpeedCache[r.Uri] = r;
                    }
                    _isTestingNodes = false;
                    ShowToast("节点测速完成", "success");
                    if (_currentPageIndex == 1) RenderPageContent(1, true);
                }));
            });
        }

        // =========================================================================
        // PAGE 2: 平板配对 (Offline QR + Pairing Parameters + Proxy Direct Rules)
        // =========================================================================
        private UIElement BuildPairingPage()
        {
            StackPanel page = new StackPanel();
            EasyTierConfig cfg = _snapshot.Config;

            DockPanel head = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
            Button copyAllBtn = UiFactory.CreateButton("复制全部参数", "primary", (s, e) =>
            {
                CopyText(BackendService.BuildPairingSummaryText(cfg), "配对参数");
            }, 30);
            DockPanel.SetDock(copyAllBtn, Dock.Right);
            head.Children.Add(copyAllBtn);
            head.Children.Add(UiFactory.CreatePageHeading("平板配对"));
            page.Children.Add(head);

            // Top 2-column grid: QR (left) + Parameters (right)
            Grid topGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: QR Card
            StackPanel qrStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock qrTitle = UiFactory.CreateTitle("离线配对二维码", 13.5);
            qrTitle.HorizontalAlignment = HorizontalAlignment.Center;
            qrTitle.Margin = new Thickness(0, 0, 0, 10);
            qrStack.Children.Add(qrTitle);

            string qrPayload = _qrJsonMode ? BackendService.BuildPairingJsonText(cfg) : BackendService.BuildPairingSummaryText(cfg);
            BitmapSource qrBmp = QrCodeEncoder.RenderBitmap(qrPayload, 5, 4);

            Border qrFrame = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(230, 232, 238)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Image qrImg = new Image
            {
                Source = qrBmp,
                Width = 172,
                Height = 172,
                Stretch = Stretch.Uniform
            };
            RenderOptions.SetBitmapScalingMode(qrImg, BitmapScalingMode.NearestNeighbor);
            qrFrame.Child = qrImg;
            qrStack.Children.Add(qrFrame);

            StackPanel qrModeBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 8) };
            Button txtModeBtn = UiFactory.CreateButton("文本", !_qrJsonMode ? "primary" : "secondary", (s, e) => { _qrJsonMode = false; RenderPageContent(2, true); }, 26, 60);
            txtModeBtn.Margin = new Thickness(0, 0, 6, 0);
            Button jsonModeBtn = UiFactory.CreateButton("JSON", _qrJsonMode ? "primary" : "secondary", (s, e) => { _qrJsonMode = true; RenderPageContent(2, true); }, 26, 60);
            qrModeBtns.Children.Add(txtModeBtn);
            qrModeBtns.Children.Add(jsonModeBtn);
            qrStack.Children.Add(qrModeBtns);

            Button saveQrBtn = UiFactory.CreateButton("保存到桌面", "secondary", (s, e) =>
            {
                try
                {
                    string outPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "远程控制配对二维码.png");
                    BitmapSource hiRes = QrCodeEncoder.RenderBitmap(qrPayload, 8, 4);
                    QrCodeEncoder.SavePng(hiRes, outPath);
                    ShowToast("已保存到桌面: 远程控制配对二维码.png", "success");
                }
                catch (Exception ex)
                {
                    ShowToast("保存失败: " + ex.Message, "error");
                }
            }, 28, 126);
            qrStack.Children.Add(saveQrBtn);

            Border qrCard = UiFactory.CreateCard(qrStack, new Thickness(14, 14, 14, 14), new Thickness(0));
            Grid.SetColumn(qrCard, 0);
            topGrid.Children.Add(qrCard);

            // Right: Parameters + Key Tips
            StackPanel paramStack = new StackPanel();
            paramStack.Children.Add(UiFactory.CreateTitle("平板端连接参数", 13.5));

            StackPanel paramRows = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            paramRows.Children.Add(CreateParamRow("主机 IP (Artemis)", cfg.HostIpOnly, cfg.HostIpOnly, false, null, true));
            paramRows.Children.Add(CreateParamRow("平板 IP (EasyTier)", cfg.SuggestedTabletIp + " " + cfg.CidrPrefix, cfg.SuggestedTabletIp, false, null, true));
            paramRows.Children.Add(CreateParamRow("网络名称", string.IsNullOrEmpty(cfg.NetworkName) ? "未配置" : cfg.NetworkName, cfg.NetworkName, false, null, true));
            paramRows.Children.Add(CreateParamRow(
                "网络密钥",
                string.IsNullOrEmpty(cfg.NetworkSecret) ? "未配置" : (_showNetworkSecret ? cfg.NetworkSecret : "••••••••••••••••"),
                cfg.NetworkSecret,
                _showNetworkSecret,
                () => { _showNetworkSecret = !_showNetworkSecret; RenderPageContent(2, true); },
                true));
            paramRows.Children.Add(CreateParamRow("公共节点", cfg.PrimaryPeer, cfg.PrimaryPeer, false, null, false));
            paramStack.Children.Add(paramRows);

            Border tipBox = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 10, 0, 0)
            };
            tipBox.SetResourceReference(Border.BackgroundProperty, "Brush.SubtleBg");
            tipBox.Child = UiFactory.CreateSubtle(
                "配对要点：\n" +
                "1. 安卓 EasyTier 设「省电无限制」+ 开启自启 + 多任务锁定防杀后台。\n" +
                "2. Apollo 网页后台 PIN 页默认在 OTP Pairing，用 4 位数字配对请先点切换到「PIN Pairing」。\n" +
                "3. 连上后按 Win + P 选「仅第二屏幕」熄灭本机屏；画面糊/发灰时关虚拟屏 HDR 并将 Artemis 码率调至 25~40 Mbps。", 11.5);
            paramStack.Children.Add(tipBox);

            Border paramCard = UiFactory.CreateCard(paramStack, new Thickness(16, 14, 16, 14), new Thickness(0));
            Grid.SetColumn(paramCard, 2);
            topGrid.Children.Add(paramCard);
            page.Children.Add(topGrid);

            // Bottom: Proxy Direct Rules Card
            StackPanel proxyCard = new StackPanel();
            DockPanel pxHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            string yamlText = BackendService.BuildProxyYamlRules(cfg);
            Button copyYamlBtn = UiFactory.CreateButton("复制 YAML 规则", "secondary", (s, e) => CopyText(yamlText, "YAML 直连规则"), 28);
            DockPanel.SetDock(copyYamlBtn, Dock.Right);
            pxHead.Children.Add(copyYamlBtn);
            pxHead.Children.Add(UiFactory.CreateTitle("代理软件直连规则（可选）", 14));
            proxyCard.Children.Add(pxHead);

            proxyCard.Children.Add(UiFactory.CreateSubtle(
                "使用 Clash / Mihomo / FlClash 时，手动把以下 4 条加到规则顶部（图形弹窗需分 4 条填，不要把整行 YAML 贴进内容框；本工具不会改动代理配置）。", 11.6));

            StackPanel ruleList = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
            ruleList.Children.Add(CreateProxyGuiHeaderRow());
            ruleList.Children.Add(CreateProxyGuiRow("PROCESS-NAME", "easytier-core.exe", "DIRECT", true));
            ruleList.Children.Add(CreateProxyGuiRow("IP-CIDR", cfg.SubnetCidr, "DIRECT", true));
            ruleList.Children.Add(CreateProxyGuiRow("PROCESS-NAME", "apollo.exe", "DIRECT", true));
            ruleList.Children.Add(CreateProxyGuiRow("PROCESS-NAME", "sunshine.exe", "DIRECT", false));
            proxyCard.Children.Add(ruleList);

            TextBox yamlBox = new TextBox
            {
                Text = yamlText,
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = UiFactory.MonoFont,
                FontSize = 11.8,
                Padding = new Thickness(10, 8, 10, 8),
                BorderThickness = new Thickness(1),
                MinHeight = 88
            };
            yamlBox.SetResourceReference(TextBox.BackgroundProperty, "Brush.CodeBg");
            yamlBox.SetResourceReference(TextBox.ForegroundProperty, "Brush.TextPrimary");
            yamlBox.SetResourceReference(TextBox.BorderBrushProperty, "Brush.Border");
            proxyCard.Children.Add(yamlBox);

            page.Children.Add(UiFactory.CreateCard(proxyCard, new Thickness(16, 14, 16, 14), new Thickness(0)));
            return page;
        }

        private Border CreateParamRow(string label, string displayVal, string copyVal, bool isSecretVisible, Action onToggleVisibility, bool divider)
        {
            Border b = new Border
            {
                Padding = new Thickness(0, 7, 0, 7),
                BorderThickness = new Thickness(0, 0, 0, divider ? 1 : 0)
            };
            b.SetResourceReference(Border.BorderBrushProperty, "Brush.Divider");

            DockPanel d = new DockPanel();
            StackPanel rightBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (onToggleVisibility != null)
            {
                Button visBtn = UiFactory.CreateButton(isSecretVisible ? "隐藏" : "显示", "ghost", (s, e) => onToggleVisibility(), 25);
                visBtn.Margin = new Thickness(0, 0, 2, 0);
                rightBtns.Children.Add(visBtn);
            }
            Button cp = UiFactory.CreateButton("复制", "secondary", (s, e) => CopyText(copyVal, label), 25);
            rightBtns.Children.Add(cp);
            DockPanel.SetDock(rightBtns, Dock.Right);
            d.Children.Add(rightBtns);

            Grid g = new Grid { VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lb = UiFactory.CreateSubtle(label, 12, false);
            lb.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(lb, 0);
            g.Children.Add(lb);

            TextBlock vl = new TextBlock
            {
                Text = displayVal,
                FontFamily = UiFactory.MonoFont,
                FontSize = 12.6,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 8, 0)
            };
            vl.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            Grid.SetColumn(vl, 1);
            g.Children.Add(vl);

            d.Children.Add(g);
            b.Child = d;
            return b;
        }

        private Border CreateProxyGuiHeaderRow()
        {
            Border b = new Border
            {
                Padding = new Thickness(0, 0, 0, 6),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            b.SetResourceReference(Border.BorderBrushProperty, "Brush.Divider");

            DockPanel d = new DockPanel();
            TextBlock opH = UiFactory.CreateSubtle("操作", 11.2, false);
            opH.Width = 68;
            opH.TextAlignment = TextAlignment.Center;
            DockPanel.SetDock(opH, Dock.Right);
            d.Children.Add(opH);

            Grid g = new Grid { VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });

            TextBlock h1 = UiFactory.CreateSubtle("代理类型", 11.2, false);
            Grid.SetColumn(h1, 0);
            g.Children.Add(h1);
            TextBlock h2 = UiFactory.CreateSubtle("内容", 11.2, false);
            Grid.SetColumn(h2, 1);
            g.Children.Add(h2);
            TextBlock h3 = UiFactory.CreateSubtle("分流策略", 11.2, false);
            Grid.SetColumn(h3, 2);
            g.Children.Add(h3);

            d.Children.Add(g);
            b.Child = d;
            return b;
        }

        private Border CreateProxyGuiRow(string ruleType, string content, string policy, bool divider)
        {
            Border b = new Border
            {
                Padding = new Thickness(0, 6, 0, 6),
                BorderThickness = new Thickness(0, 0, 0, divider ? 1 : 0)
            };
            b.SetResourceReference(Border.BorderBrushProperty, "Brush.Divider");

            DockPanel d = new DockPanel();
            Button cp = UiFactory.CreateButton("复制内容", "ghost", (s, e) => CopyText(content, content), 24);
            DockPanel.SetDock(cp, Dock.Right);
            d.Children.Add(cp);

            Grid g = new Grid { VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });

            Border tTag = UiFactory.CreateTag(ruleType, "accent");
            tTag.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(tTag, 0);
            g.Children.Add(tTag);

            TextBlock cTxt = new TextBlock
            {
                Text = content,
                FontFamily = UiFactory.MonoFont,
                FontSize = 12.2,
                VerticalAlignment = VerticalAlignment.Center
            };
            cTxt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            Grid.SetColumn(cTxt, 1);
            g.Children.Add(cTxt);

            TextBlock pTxt = UiFactory.CreateSubtle(policy, 11.8, false);
            pTxt.FontFamily = UiFactory.MonoFont;
            pTxt.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(pTxt, 2);
            g.Children.Add(pTxt);

            d.Children.Add(g);
            b.Child = d;
            return b;
        }

        // =========================================================================
        // PAGE 3: 设置 (Clean Native Desktop Settings)
        // =========================================================================
        private UIElement BuildSettingsPage()
        {
            StackPanel page = new StackPanel();
            TextBlock heading = UiFactory.CreatePageHeading("设置");
            heading.Margin = new Thickness(0, 0, 0, 20);
            page.Children.Add(heading);

            // 1. 外观
            StackPanel appCard = new StackPanel();
            appCard.Children.Add(UiFactory.CreateTitle("外观", 14));

            DockPanel themeRow = new DockPanel { Margin = new Thickness(0, 12, 0, 10) };
            StackPanel themeBtns = new StackPanel { Orientation = Orientation.Horizontal };
            Button lightBtn = UiFactory.CreateButton("浅色", !ThemeManager.IsDark ? "primary" : "secondary", (s, e) =>
            {
                _settings.Theme = "Light";
                _settings.Save(_backend.SettingsPath);
                ApplyThemeAndRebuild(false);
                ShowToast("已切换浅色模式", "success");
            }, 28, 64);
            lightBtn.Margin = new Thickness(0, 0, 6, 0);
            Button darkBtn = UiFactory.CreateButton("深色", ThemeManager.IsDark ? "primary" : "secondary", (s, e) =>
            {
                _settings.Theme = "Dark";
                _settings.Save(_backend.SettingsPath);
                ApplyThemeAndRebuild(true);
                ShowToast("已切换深色模式", "success");
            }, 28, 64);
            themeBtns.Children.Add(lightBtn);
            themeBtns.Children.Add(darkBtn);
            DockPanel.SetDock(themeBtns, Dock.Right);
            themeRow.Children.Add(themeBtns);

            TextBlock themeTitle = UiFactory.CreateTitle("界面主题", 13);
            themeTitle.FontWeight = FontWeights.Normal;
            themeTitle.VerticalAlignment = VerticalAlignment.Center;
            themeRow.Children.Add(themeTitle);
            appCard.Children.Add(themeRow);

            appCard.Children.Add(UiFactory.CreateDivider());

            DockPanel titleRow = new DockPanel { Margin = new Thickness(0, 10, 0, 2) };
            StackPanel titleInputs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBox titleBox = UiFactory.CreateTextBox(_customTitleDraft ?? _settings.AppTitle, 30);
            titleBox.Width = 190;
            titleBox.Margin = new Thickness(0, 0, 6, 0);
            titleBox.TextChanged += (s, e) => { _customTitleDraft = titleBox.Text ?? ""; };
            Action saveTitleAction = () =>
            {
                string t = (titleBox.Text ?? "").Trim();
                if (string.IsNullOrEmpty(t)) t = "远程控制中心";
                _settings.AppTitle = t;
                _customTitleDraft = t;
                _settings.Save(_backend.SettingsPath);
                Title = t;
                if (_brandTitleText != null) _brandTitleText.Text = t;
                if (_trayIcon != null) _trayIcon.Text = TrimTrayText(t);
                ShowToast("窗口标题已更新", "success");
            };
            titleBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { saveTitleAction(); e.Handled = true; } };
            Button saveTitleBtn = UiFactory.CreateButton("保存", "primary", (s, e) => saveTitleAction(), 30, 56);
            saveTitleBtn.Margin = new Thickness(0, 0, 6, 0);
            Button resetTitleBtn = UiFactory.CreateButton("默认", "secondary", (s, e) =>
            {
                _settings.AppTitle = "远程控制中心";
                _customTitleDraft = _settings.AppTitle;
                _settings.Save(_backend.SettingsPath);
                Title = _settings.AppTitle;
                if (_brandTitleText != null) _brandTitleText.Text = _settings.AppTitle;
                if (_trayIcon != null) _trayIcon.Text = TrimTrayText(_settings.AppTitle);
                RenderPageContent(3, true);
                ShowToast("已恢复默认标题", "info");
            }, 30, 56);
            titleInputs.Children.Add(titleBox);
            titleInputs.Children.Add(saveTitleBtn);
            titleInputs.Children.Add(resetTitleBtn);
            DockPanel.SetDock(titleInputs, Dock.Right);
            titleRow.Children.Add(titleInputs);

            TextBlock titleLbl = UiFactory.CreateTitle("窗口标题", 13);
            titleLbl.FontWeight = FontWeights.Normal;
            titleLbl.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(titleLbl);
            appCard.Children.Add(titleRow);

            page.Children.Add(UiFactory.CreateCard(appCard, new Thickness(16, 14, 16, 14), new Thickness(0, 0, 0, 12)));

            // 2. 常规与托盘
            StackPanel behCard = new StackPanel();
            behCard.Children.Add(UiFactory.CreateTitle("常规", 14));

            behCard.Children.Add(CreateSettingToggleRow(
                "关闭窗口时最小化到托盘",
                "点击右上角关闭后继续在托盘后台运行",
                _settings.MinimizeToTrayOnClose,
                val =>
                {
                    _settings.MinimizeToTrayOnClose = val;
                    _settings.Save(_backend.SettingsPath);
                    if (_closeWinBtn != null) _closeWinBtn.ToolTip = val ? "最小化到托盘" : "关闭";
                    ShowToast(val ? "已开启关闭到托盘" : "关闭窗口将直接退出", "success");
                },
                true));

            behCard.Children.Add(CreateSettingToggleRow(
                "开机自动静默到托盘",
                "登录后自动在后台启动，不弹出主窗口",
                _snapshot.UiAutostart,
                val =>
                {
                    RunBusyAction(
                        "正在更新开机自启...",
                        () => _backend.SetUiAutostart(val, true),
                        true,
                        res =>
                        {
                            if (res.Ok)
                            {
                                _settings.StartMinimizedAutostart = val;
                                _settings.Save(_backend.SettingsPath);
                            }
                        });
                },
                true));

            DockPanel pollRow = new DockPanel { Margin = new Thickness(0, 10, 0, 2) };
            StackPanel pollBtns = new StackPanel { Orientation = Orientation.Horizontal };
            int[] intervals = new int[] { 3, 5, 10 };
            for (int i = 0; i < intervals.Length; i++)
            {
                int sec = intervals[i];
                Button b = UiFactory.CreateButton(sec + " 秒", _settings.RefreshIntervalSeconds == sec ? "primary" : "secondary", (s, e) =>
                {
                    _settings.RefreshIntervalSeconds = sec;
                    _settings.Save(_backend.SettingsPath);
                    if (_refreshTimer != null) _refreshTimer.Interval = TimeSpan.FromSeconds(sec);
                    RenderPageContent(3, true);
                    ShowToast(string.Format("刷新间隔已设为 {0} 秒", sec), "success");
                }, 27, 52);
                b.Margin = new Thickness(0, 0, 6, 0);
                pollBtns.Children.Add(b);
            }
            DockPanel.SetDock(pollBtns, Dock.Right);
            pollRow.Children.Add(pollBtns);

            StackPanel pollLbl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock pTitle = UiFactory.CreateTitle("状态刷新间隔", 13);
            pTitle.FontWeight = FontWeights.Normal;
            pollLbl.Children.Add(pTitle);
            pollLbl.Children.Add(UiFactory.CreateSubtle("仅在主窗口打开时自动刷新", 11.4));
            pollRow.Children.Add(pollLbl);
            behCard.Children.Add(pollRow);

            page.Children.Add(UiFactory.CreateCard(behCard, new Thickness(16, 14, 16, 14), new Thickness(0, 0, 0, 12)));

            // 3. 目录与维护
            StackPanel maintCard = new StackPanel();
            maintCard.Children.Add(UiFactory.CreateTitle("目录与维护", 14));

            Border envBox = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 10, 0, 12)
            };
            envBox.SetResourceReference(Border.BackgroundProperty, "Brush.SubtleBg");
            envBox.Child = UiFactory.CreateSubtle(
                string.Format("工作目录：{0}\n当前权限：{1}   |   免弹窗启动任务：{2}",
                    _backend.WorkspaceRoot,
                    _snapshot.IsAdmin ? "管理员" : "标准用户",
                    _snapshot.ElevatedTaskReady ? "已就绪" : "未注册"), 11.8);
            maintCard.Children.Add(envBox);

            WrapPanel maintBtns = new WrapPanel();
            AddMaintBtn(maintBtns, "打开安装目录", "secondary", (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _backend.WorkspaceRoot + "\"") { UseShellExecute = true }); }
                catch { }
            });
            AddMaintBtn(maintBtns, "查看操作手册", "secondary", (s, e) =>
            {
                string doc = Path.Combine(_backend.WorkspaceRoot, "我的配对信息与避坑手册.txt");
                if (!File.Exists(doc)) doc = Path.Combine(_backend.WorkspaceRoot, "操作手册.txt");
                if (!File.Exists(doc)) doc = Path.Combine(_backend.WorkspaceRoot, "我的远程配对信息.txt");
                if (File.Exists(doc))
                {
                    try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + doc + "\"") { UseShellExecute = true }); }
                    catch (Exception ex) { ShowToast("打开失败: " + ex.Message, "error"); }
                }
                else ShowToast("未找到操作手册文件", "error");
            });
            AddMaintBtn(maintBtns, "注册免弹窗启动", "secondary", (s, e) =>
            {
                RunBusyAction("正在注册免弹窗启动任务...", () => _backend.RegisterSilentElevatedLauncher(), true);
            });
            AddMaintBtn(maintBtns, "GitHub 开源项目", "secondary", (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://github.com/qianxuxuxu/Apollo-EasyTier-Remote") { UseShellExecute = true }); }
                catch { }
            });
            AddMaintBtn(maintBtns, "退出程序", "danger", (s, e) => ExitApplication());

            maintCard.Children.Add(maintBtns);
            page.Children.Add(UiFactory.CreateCard(maintCard, new Thickness(16, 14, 16, 14), new Thickness(0)));

            return page;
        }

        private static void AddMaintBtn(WrapPanel panel, string text, string variant, RoutedEventHandler onClick)
        {
            Button b = UiFactory.CreateButton(text, variant, onClick, 30);
            b.Margin = new Thickness(0, 0, 8, 6);
            panel.Children.Add(b);
        }

        private Border CreateSettingToggleRow(string title, string desc, bool isOn, Action<bool> onChanged, bool showDivider)
        {
            Border b = new Border
            {
                Padding = new Thickness(0, 10, 0, 10),
                BorderThickness = new Thickness(0, 0, 0, showDivider ? 1 : 0)
            };
            b.SetResourceReference(Border.BorderBrushProperty, "Brush.Divider");

            DockPanel d = new DockPanel();
            Border sw = UiFactory.CreateToggleSwitch(isOn, onChanged);
            DockPanel.SetDock(sw, Dock.Right);
            d.Children.Add(sw);

            StackPanel info = new StackPanel { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            TextBlock t = UiFactory.CreateTitle(title, 13);
            t.FontWeight = FontWeights.Normal;
            info.Children.Add(t);
            info.Children.Add(UiFactory.CreateSubtle(desc, 11.4));
            d.Children.Add(info);

            b.Child = d;
            return b;
        }
    }
}
