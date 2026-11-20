using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace RemoteControlUI
{
    public partial class MainWindow : Window
    {
        private readonly BackendService _backend;
        private UiSettings _settings;
        private RuntimeSnapshot _snapshot;

        private int _currentPageIndex = 0; // 0:全部设备, 1:网络与服务, 2:平板配对, 3:设置
        private bool _showNetworkName = true;
        private bool _showNetworkSecret = false;
        private bool _showHostDetail = false;
        private bool _showPublicRelays = false;
        private bool _navDevicesExpanded = true;
        private bool _navToolsExpanded = true;
        private bool _secHostExpanded = true;
        private bool _secRemoteExpanded = true;
        private bool _secServicesExpanded = true;
        private string _expandedRemotePeerKey = null;
        private bool _qrJsonMode = false;
        private bool _isExiting = false;
        private bool _isRefreshing = false;
        private bool _pendingRefresh = false;
        private bool _pendingRefreshSlow = false;
        private bool _isTestingNodes = false;
        private bool _initialRefreshDone = false;
        private string _customNodeDraft = "";
        private string _customTitleDraft = null;

        private readonly Dictionary<string, NodeSpeedResult> _nodeSpeedCache = new Dictionary<string, NodeSpeedResult>(StringComparer.OrdinalIgnoreCase);

        // Shell UI references
        private Grid _rootGrid;
        private readonly Dictionary<int, Border> _navItems = new Dictionary<int, Border>();
        private readonly Dictionary<int, string> _navIcons = new Dictionary<int, string>();
        private TextBlock _brandTitleText;
        private ContentControl _pageHost;
        private ScrollViewer _contentScroll;
        private Border _toastBanner;
        private TextBlock _toastText;
        private DispatcherTimer _toastTimer;
        private DispatcherTimer _refreshTimer;
        private Button _maxWinBtn;
        private Button _closeWinBtn;

        // Tray
        private Forms.NotifyIcon _trayIcon;
        private Forms.ContextMenuStrip _trayMenu;
        private System.Drawing.Icon _trayCustomIcon;
        private bool _trayHintShown = false;

        public MainWindow(BackendService backend, UiSettings settings, bool startMinimized)
        {
            _backend = backend;
            _settings = settings;
            _customTitleDraft = _settings.AppTitle;
            _snapshot = new RuntimeSnapshot();
            _snapshot.IsAdmin = BackendService.IsAdministrator();
            _snapshot.WorkspaceRoot = _backend.WorkspaceRoot;
            _snapshot.Config = _backend.LoadConfig();

            Title = _settings.AppTitle;
            Width = 980;
            Height = 680;
            MinWidth = 860;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ResizeMode = ResizeMode.CanResize;
            FontFamily = UiFactory.SansFont;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            WindowChrome chrome = new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(10),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(this, chrome);

            ThemeManager.ApplyTheme(string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase), this);
            SetResourceReference(Window.BackgroundProperty, "Brush.ShellBg");

            TryLoadWindowIcon();
            BuildShell();
            InitTrayIcon();
            InitTimers();

            SourceInitialized += (s, e) =>
            {
                ThemeManager.ApplyDwmWindowStyle(this, ThemeManager.IsDark);
            };

            StateChanged += (s, e) =>
            {
                UpdateMaximizedPadding();
            };

            Closing += MainWindow_Closing;

            if (startMinimized)
            {
                ShowInTaskbar = false;
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (!_initialRefreshDone)
                    {
                        _initialRefreshDone = true;
                        RefreshSnapshotAsync(true, false);
                    }
                }), DispatcherPriority.Background);
            }

            Loaded += (s, e) =>
            {
                if (!_initialRefreshDone)
                {
                    _initialRefreshDone = true;
                    RefreshSnapshotAsync(true, false);
                }
            };
        }

        private void UpdateMaximizedPadding()
        {
            bool isMax = (WindowState == WindowState.Maximized);
            if (_rootGrid != null)
            {
                _rootGrid.Margin = isMax ? new Thickness(7) : new Thickness(0);
            }
            if (_maxWinBtn != null)
            {
                _maxWinBtn.Content = UiIcons.Create(isMax ? UiIcons.WinRestore : UiIcons.WinMaximize, 11, new SolidColorBrush(ThemeManager.TextSecondary), 1.6);
                _maxWinBtn.ToolTip = isMax ? "还原" : "最大化";
            }
        }

        private void TryLoadWindowIcon()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                using (System.Drawing.Icon ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                {
                    if (ico != null)
                    {
                        Icon = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    }
                }
            }
            catch { }
        }

        private void InitTrayIcon()
        {
            try
            {
                _trayMenu = new Forms.ContextMenuStrip();
                _trayMenu.Items.Add("打开主界面", null, (s, e) => Dispatcher.Invoke((Action)RestoreFromTray));
                _trayMenu.Items.Add("复制本机虚拟 IP", null, (s, e) => Dispatcher.Invoke((Action)(() =>
                {
                    CopyText(_snapshot.Config.HostIpOnly, "本机 IP");
                })));
                _trayMenu.Items.Add("打开 Apollo 后台", null, (s, e) => Dispatcher.Invoke((Action)OpenApolloWeb));
                _trayMenu.Items.Add("重启 EasyTier", null, (s, e) => Dispatcher.Invoke((Action)(() =>
                {
                    RunBusyAction("正在重启 EasyTier...", () => _backend.RestartEasyTier(), true);
                })));
                _trayMenu.Items.Add("切换深浅主题", null, (s, e) => Dispatcher.Invoke((Action)ToggleTheme));
                _trayMenu.Items.Add(new Forms.ToolStripSeparator());
                _trayMenu.Items.Add("退出", null, (s, e) => Dispatcher.Invoke((Action)ExitApplication));

                System.Drawing.Icon trayIco = null;
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    _trayCustomIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    trayIco = _trayCustomIcon;
                }
                catch { }
                if (trayIco == null) trayIco = System.Drawing.SystemIcons.Application;

                _trayIcon = new Forms.NotifyIcon
                {
                    Icon = trayIco,
                    Text = TrimTrayText(_settings.AppTitle),
                    Visible = true,
                    ContextMenuStrip = _trayMenu
                };
                _trayIcon.DoubleClick += (s, e) => Dispatcher.Invoke((Action)RestoreFromTray);
                _trayIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Left)
                    {
                        Dispatcher.Invoke((Action)RestoreFromTray);
                    }
                };
            }
            catch { }
        }

        private static string TrimTrayText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "远程控制中心";
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        public void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting && _settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                if (!_trayHintShown && _trayIcon != null)
                {
                    _trayHintShown = true;
                    try
                    {
                        _trayIcon.ShowBalloonTip(2000, _settings.AppTitle, "已最小化到系统托盘，点击图标可随时恢复。", Forms.ToolTipIcon.Info);
                    }
                    catch { }
                }
                return;
            }
            _isExiting = true;
            if (_refreshTimer != null) _refreshTimer.Stop();
            if (_toastTimer != null) _toastTimer.Stop();
            CleanupTray();
            if (Application.Current != null)
            {
                try { Application.Current.Shutdown(); } catch { }
            }
        }

        public void ExitApplication()
        {
            _isExiting = true;
            if (_refreshTimer != null) _refreshTimer.Stop();
            if (_toastTimer != null) _toastTimer.Stop();
            CleanupTray();
            try { Close(); } catch { }
            if (Application.Current != null)
            {
                try { Application.Current.Shutdown(); } catch { }
            }
        }

        private void CleanupTray()
        {
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
                if (_trayMenu != null)
                {
                    _trayMenu.Dispose();
                    _trayMenu = null;
                }
                if (_trayCustomIcon != null)
                {
                    _trayCustomIcon.Dispose();
                    _trayCustomIcon = null;
                }
            }
            catch { }
        }

        private void InitTimers()
        {
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                if (_toastBanner != null) _toastBanner.Visibility = Visibility.Collapsed;
            };

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds) };
            _refreshTimer.Tick += (s, e) =>
            {
                if (IsVisible) RefreshSnapshotAsync(false, true);
            };
            _refreshTimer.Start();
        }

        private void BuildShell()
        {
            // Unified UU Remote shell: full-width subtle gray title bar + left sidebar, crisp white rounded-top-left workspace
            _rootGrid = new Grid();
            _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(204) });
            _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _rootGrid.SetResourceReference(Grid.BackgroundProperty, "Brush.ShellBg");

            // 1. Full-Width Top Title Bar (spans both columns)
            Border topBar = new Border
            {
                Padding = new Thickness(16, 0, 10, 0),
                Background = Brushes.Transparent
            };
            topBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) ToggleMaximize();
                else if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            DockPanel topDock = new DockPanel { LastChildFill = true };

            // Top-Left Brand Mark + App Title
            StackPanel brandRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Border markWrap = new Border
            {
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = UiIcons.CreateFilledBrandMark(19)
            };
            _brandTitleText = new TextBlock
            {
                Text = _settings.AppTitle,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                FontFamily = UiFactory.SansFont,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 360,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _brandTitleText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            brandRow.Children.Add(markWrap);
            brandRow.Children.Add(_brandTitleText);
            DockPanel.SetDock(brandRow, Dock.Left);
            topDock.Children.Add(brandRow);

            // Top-Right Minimal Icon Toolbar + Window Controls (UU Remote style)
            StackPanel topRight = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Button apolloIconBtn = UiFactory.CreateIconButton(UiIcons.External, "打开 Apollo 管理后台 (localhost:47990)", (s, e) => OpenApolloWeb(), 30, 14.5);
            apolloIconBtn.Margin = new Thickness(0, 0, 2, 0);
            Button refreshIconBtn = UiFactory.CreateIconButton(UiIcons.Refresh, "刷新状态", (s, e) => RefreshSnapshotAsync(true, false), 30, 14.5);
            refreshIconBtn.Margin = new Thickness(0, 0, 2, 0);
            Button themeIconBtn = UiFactory.CreateIconButton(
                ThemeManager.IsDark ? UiIcons.Sun : UiIcons.Moon,
                ThemeManager.IsDark ? "切换浅色模式" : "切换深色模式",
                (s, e) => ToggleTheme(), 30, 14.5);
            themeIconBtn.Margin = new Thickness(0, 0, 8, 0);

            Border sep = new Border
            {
                Width = 1,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            sep.SetResourceReference(Border.BackgroundProperty, "Brush.Border");

            topRight.Children.Add(apolloIconBtn);
            topRight.Children.Add(refreshIconBtn);
            topRight.Children.Add(themeIconBtn);
            topRight.Children.Add(sep);
            topRight.Children.Add(CreateWinControlButton(UiIcons.WinMinimize, "最小化", (s, e) => WindowState = WindowState.Minimized, false));
            _maxWinBtn = CreateWinControlButton(WindowState == WindowState.Maximized ? UiIcons.WinRestore : UiIcons.WinMaximize, WindowState == WindowState.Maximized ? "还原" : "最大化", (s, e) => ToggleMaximize(), false);
            topRight.Children.Add(_maxWinBtn);
            _closeWinBtn = CreateWinControlButton(UiIcons.WinClose, _settings.MinimizeToTrayOnClose ? "最小化到托盘" : "关闭", (s, e) => Close(), true);
            topRight.Children.Add(_closeWinBtn);

            DockPanel.SetDock(topRight, Dock.Right);
            topDock.Children.Add(topRight);

            // Empty draggable spacer in center
            topDock.Children.Add(new Border { Background = Brushes.Transparent });
            topBar.Child = topDock;

            Grid.SetRow(topBar, 0);
            Grid.SetColumnSpan(topBar, 2);
            _rootGrid.Children.Add(topBar);

            // 2. Left Sidebar (clean minimalist navigation + pinned bottom Settings, like UU Remote)
            DockPanel sideDock = new DockPanel { LastChildFill = true };

            _navItems.Clear();
            _navIcons.Clear();

            // Pinned bottom "设置" row with thin top divider (left padding 0 so ActiveBar aligns flush-left)
            Border bottomWrap = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 8, 10)
            };
            bottomWrap.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            bottomWrap.Child = CreateNavItem(3, "设置", UiIcons.Settings, false);
            DockPanel.SetDock(bottomWrap, Dock.Bottom);
            sideDock.Children.Add(bottomWrap);

            // Upper navigation groups (genuinely collapsible like UU Remote)
            StackPanel navStack = new StackPanel { Margin = new Thickness(0, 8, 8, 8) };
            StackPanel devItems = new StackPanel { Visibility = _navDevicesExpanded ? Visibility.Visible : Visibility.Collapsed };
            devItems.Children.Add(CreateNavItem(0, "全部设备", UiIcons.AllDevices, true));
            navStack.Children.Add(CreateSidebarSectionLabel("我的设备", UiIcons.DevicesGroup, _navDevicesExpanded, () =>
            {
                _navDevicesExpanded = !_navDevicesExpanded;
                devItems.Visibility = _navDevicesExpanded ? Visibility.Visible : Visibility.Collapsed;
            }));
            navStack.Children.Add(devItems);

            StackPanel toolItems = new StackPanel { Visibility = _navToolsExpanded ? Visibility.Visible : Visibility.Collapsed };
            toolItems.Children.Add(CreateNavItem(2, "平板配对", UiIcons.QrPairing, true));
            toolItems.Children.Add(CreateNavItem(1, "网络与服务", UiIcons.Network, true));
            UIElement sec2 = CreateSidebarSectionLabel("连接与工具", UiIcons.LinkGroup, _navToolsExpanded, () =>
            {
                _navToolsExpanded = !_navToolsExpanded;
                toolItems.Visibility = _navToolsExpanded ? Visibility.Visible : Visibility.Collapsed;
            });
            ((FrameworkElement)sec2).Margin = new Thickness(14, 14, 10, 6);
            navStack.Children.Add(sec2);
            navStack.Children.Add(toolItems);

            sideDock.Children.Add(navStack);

            Grid.SetRow(sideDock, 1);
            Grid.SetColumn(sideDock, 0);
            _rootGrid.Children.Add(sideDock);

            // 3. Right Main Content Workspace (crisp white sheet with rounded top-left corner)
            Border contentSheet = new Border
            {
                CornerRadius = new CornerRadius(10, 0, 0, 0),
                BorderThickness = new Thickness(1, 1, 0, 0),
                ClipToBounds = true
            };
            contentSheet.SetResourceReference(Border.BackgroundProperty, "Brush.ContentBg");
            contentSheet.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");

            Grid contentOverlay = new Grid();
            _pageHost = new ContentControl();
            _contentScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(32, 24, 32, 28),
                Content = _pageHost
            };
            contentOverlay.Children.Add(_contentScroll);

            // Minimalist Toast Notification
            _toastBanner = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 9, 14, 9),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 24, 20),
                Visibility = Visibility.Collapsed,
                Background = new SolidColorBrush(Color.FromRgb(29, 33, 41))
            };
            _toastText = new TextBlock
            {
                Text = "",
                Foreground = Brushes.White,
                FontFamily = UiFactory.SansFont,
                FontSize = 12.2,
                FontWeight = FontWeights.Normal
            };
            _toastBanner.Child = _toastText;
            contentOverlay.Children.Add(_toastBanner);

            contentSheet.Child = contentOverlay;
            Grid.SetRow(contentSheet, 1);
            Grid.SetColumn(contentSheet, 1);
            _rootGrid.Children.Add(contentSheet);

            Content = _rootGrid;
            UpdateMaximizedPadding();
            RenderPageContent(_currentPageIndex, false);
        }

        private UIElement CreateSidebarSectionLabel(string text, string iconPath, bool initialExpanded, Action onToggle)
        {
            bool expanded = initialExpanded;
            DockPanel dp = new DockPanel
            {
                Margin = new Thickness(14, 2, 10, 6),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };
            Border chevWrap = new Border
            {
                VerticalAlignment = VerticalAlignment.Center,
                Child = UiIcons.Create(expanded ? UiIcons.ChevronUp : UiIcons.ChevronDown, 12, new SolidColorBrush(ThemeManager.TextMuted), 1.6)
            };
            DockPanel.SetDock(chevWrap, Dock.Right);
            dp.Children.Add(chevWrap);

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Border ic = new Border
            {
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = UiIcons.Create(iconPath, 14, new SolidColorBrush(ThemeManager.TextSecondary), 1.6)
            };
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontFamily = UiFactory.SansFont,
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            left.Children.Add(ic);
            left.Children.Add(tb);
            dp.Children.Add(left);

            dp.MouseEnter += (s, e) => tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            dp.MouseLeave += (s, e) => tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            dp.MouseLeftButtonUp += (s, e) =>
            {
                expanded = !expanded;
                chevWrap.Child = UiIcons.Create(expanded ? UiIcons.ChevronUp : UiIcons.ChevronDown, 12, new SolidColorBrush(ThemeManager.TextMuted), 1.6);
                if (onToggle != null) onToggle();
            };
            return dp;
        }

        private Border CreateNavItem(int index, string label, string iconPath, bool indented)
        {
            // Outer wrapper holds the flush-left blue active indicator bar + rounded pill row
            Border outer = new Border
            {
                Height = 36,
                Margin = new Thickness(0, 1, 0, 2),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Tag = index
            };

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left blue indicator bar (3px wide, 16px tall, visible only when active — UU Remote signature!)
            Border bar = new Border
            {
                Name = "ActiveBar",
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(ThemeManager.Accent),
                Visibility = Visibility.Hidden
            };
            Grid.SetColumn(bar, 0);
            g.Children.Add(bar);

            Border pill = new Border
            {
                Name = "NavPill",
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(indented ? 22 : 10, 0, 10, 0),
                Background = Brushes.Transparent
            };

            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Border iconWrap = new Border
            {
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = UiIcons.Create(iconPath, 15.5, new SolidColorBrush(ThemeManager.TextSecondary), 1.7)
            };
            TextBlock tb = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontFamily = UiFactory.SansFont,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            sp.Children.Add(iconWrap);
            sp.Children.Add(tb);
            pill.Child = sp;

            Grid.SetColumn(pill, 1);
            g.Children.Add(pill);
            outer.Child = g;

            outer.MouseLeftButtonUp += (s, e) => SelectPage(index);
            outer.MouseEnter += (s, e) =>
            {
                if (_currentPageIndex != index)
                {
                    pill.Background = new SolidColorBrush(ThemeManager.NavHoverBg);
                }
            };
            outer.MouseLeave += (s, e) =>
            {
                if (_currentPageIndex != index)
                {
                    pill.Background = Brushes.Transparent;
                }
            };

            _navItems[index] = outer;
            _navIcons[index] = iconPath;
            return outer;
        }

        private void UpdateNavStyles()
        {
            foreach (KeyValuePair<int, Border> kv in _navItems)
            {
                int idx = kv.Key;
                bool active = (idx == _currentPageIndex);
                Grid g = kv.Value.Child as Grid;
                if (g == null || g.Children.Count < 2) continue;

                Border bar = g.Children[0] as Border;
                Border pill = g.Children[1] as Border;
                if (bar != null) bar.Visibility = active ? Visibility.Visible : Visibility.Hidden;
                if (pill != null)
                {
                    pill.Background = active ? new SolidColorBrush(ThemeManager.NavActiveBg) : Brushes.Transparent;
                    StackPanel sp = pill.Child as StackPanel;
                    if (sp != null && sp.Children.Count >= 2)
                    {
                        Brush iconBrush = active ? new SolidColorBrush(ThemeManager.Accent) : new SolidColorBrush(ThemeManager.TextSecondary);
                        Brush textBrush = new SolidColorBrush(ThemeManager.TextPrimary);
                        Border iconWrap = sp.Children[0] as Border;
                        string iconPath;
                        if (iconWrap != null && _navIcons.TryGetValue(idx, out iconPath))
                        {
                            iconWrap.Child = UiIcons.Create(iconPath, 15.5, iconBrush, active ? 1.95 : 1.65);
                        }
                        TextBlock tb = sp.Children[1] as TextBlock;
                        if (tb != null)
                        {
                            tb.Foreground = textBrush;
                            tb.FontWeight = active ? FontWeights.Medium : FontWeights.Normal;
                        }
                    }
                }
            }
        }

        private Button CreateWinControlButton(string iconPath, string tooltip, RoutedEventHandler onClick, bool isClose)
        {
            Button btn = new Button
            {
                Content = UiIcons.Create(iconPath, 11, new SolidColorBrush(ThemeManager.TextSecondary), 1.65),
                ToolTip = tooltip,
                Width = 32,
                Height = 28,
                Margin = new Thickness(1, 0, 0, 0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };

            ControlTemplate tpl = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            Trigger tr = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            if (isClose)
            {
                tr.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(ThemeManager.Danger), "bd"));
            }
            else
            {
                tr.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(ThemeManager.NavHoverBg), "bd"));
            }
            tpl.Triggers.Add(tr);

            if (isClose)
            {
                btn.MouseEnter += (s, e) => btn.Content = UiIcons.Create(iconPath, 11, Brushes.White, 1.75);
                btn.MouseLeave += (s, e) => btn.Content = UiIcons.Create(iconPath, 11, new SolidColorBrush(ThemeManager.TextSecondary), 1.65);
            }

            btn.Template = tpl;
            btn.Click += onClick;
            return btn;
        }

        public void SelectPage(int index)
        {
            bool changed = (index != _currentPageIndex);
            _currentPageIndex = index;
            RenderPageContent(index, !changed);
        }

        private void RenderPageContent(int index, bool preserveScroll)
        {
            double prevOffset = (_contentScroll != null && preserveScroll) ? _contentScroll.VerticalOffset : 0;
            UpdateNavStyles();

            if (index == 0)
            {
                _pageHost.Content = BuildHomePage();
            }
            else if (index == 1)
            {
                _pageHost.Content = BuildNetworkPage();
            }
            else if (index == 2)
            {
                _pageHost.Content = BuildPairingPage();
            }
            else
            {
                _pageHost.Content = BuildSettingsPage();
            }

            if (_contentScroll != null)
            {
                if (preserveScroll && prevOffset > 0)
                {
                    _contentScroll.ScrollToVerticalOffset(prevOffset);
                }
                else
                {
                    _contentScroll.ScrollToTop();
                }
            }
        }

        private void ToggleTheme()
        {
            bool nextDark = !ThemeManager.IsDark;
            _settings.Theme = nextDark ? "Dark" : "Light";
            _settings.Save(_backend.SettingsPath);
            ApplyThemeAndRebuild(nextDark);
            ShowToast(nextDark ? "已切换深色模式" : "已切换浅色模式", "info");
        }

        public void ApplyThemeAndRebuild(bool dark)
        {
            ThemeManager.ApplyTheme(dark, this);
            BuildShell();
        }

        private void ToggleMaximize()
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        public void ShowToast(string message, string level)
        {
            if (_toastBanner == null || _toastText == null) return;
            _toastText.Text = message;
            if (level == "error") _toastBanner.Background = new SolidColorBrush(Color.FromRgb(207, 19, 34));
            else if (level == "success") _toastBanner.Background = new SolidColorBrush(Color.FromRgb(16, 134, 72));
            else _toastBanner.Background = new SolidColorBrush(Color.FromRgb(29, 33, 41));
            _toastBanner.Visibility = Visibility.Visible;
            if (_toastTimer != null)
            {
                _toastTimer.Stop();
                _toastTimer.Start();
            }
        }

        private void CopyText(string text, string label)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    ShowToast(label + " 为空", "error");
                    return;
                }
                Clipboard.SetText(text);
                ShowToast("已复制 " + label, "success");
            }
            catch (Exception ex)
            {
                ShowToast("复制失败: " + ex.Message, "error");
            }
        }

        private void OpenApolloWeb()
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://localhost:47990") { UseShellExecute = true });
                ShowToast("已打开 Apollo 管理后台", "info");
            }
            catch (Exception ex)
            {
                ShowToast("无法打开浏览器: " + ex.Message, "error");
            }
        }

        private void RefreshSnapshotAsync(bool includeSlowChecks, bool isBackgroundTick = false)
        {
            if (_isRefreshing)
            {
                if (!isBackgroundTick)
                {
                    _pendingRefresh = true;
                    _pendingRefreshSlow = _pendingRefreshSlow || includeSlowChecks;
                }
                return;
            }
            _isRefreshing = true;
            RuntimeSnapshot prev = _snapshot;
            Task.Run(() =>
            {
                RuntimeSnapshot fresh = _backend.CollectSnapshot(includeSlowChecks);
                if (!includeSlowChecks && prev != null)
                {
                    fresh.Firewall = prev.Firewall;
                    fresh.EasyTierAutostart = prev.EasyTierAutostart;
                    fresh.UiAutostart = prev.UiAutostart;
                    fresh.ElevatedTaskReady = prev.ElevatedTaskReady;
                    fresh.AcNeverSleep = prev.AcNeverSleep;
                    fresh.DisplayAdapters = prev.DisplayAdapters;
                }
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    _snapshot = fresh;
                    _isRefreshing = false;

                    bool userTyping = (Keyboard.FocusedElement is TextBox);
                    if (isBackgroundTick)
                    {
                        if (_currentPageIndex == 0 && !userTyping)
                        {
                            RenderPageContent(0, true);
                        }
                    }
                    else
                    {
                        RenderPageContent(_currentPageIndex, true);
                    }

                    if (_pendingRefresh)
                    {
                        bool nextSlow = _pendingRefreshSlow;
                        _pendingRefresh = false;
                        _pendingRefreshSlow = false;
                        RefreshSnapshotAsync(nextSlow, false);
                    }
                }));
            });
        }

        private void RunBusyAction(string progressMsg, Func<ActionResult> action, bool refreshSlowAfter, Action<ActionResult> onCompleted = null)
        {
            ShowToast(progressMsg, "info");
            Task.Run(() =>
            {
                ActionResult res = action();
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (onCompleted != null) onCompleted(res);
                    ShowToast(res.Message, res.Ok ? "success" : "error");
                    RefreshSnapshotAsync(refreshSlowAfter, false);
                }));
            });
        }

        private static string GetModeShortText(RuntimeSnapshot snap)
        {
            if (!snap.EasyTierRunning) return "组网未运行";
            if (snap.SummaryMode == "P2P") return "P2P 直连 · " + snap.SummaryLatency;
            if (snap.SummaryMode == "Relay") return "中继连接 · " + snap.SummaryLatency;
            if (snap.SummaryMode == "PublicReady") return "在线 · 待连接";
            return "本机在线";
        }

        private static string GetModeTone(RuntimeSnapshot snap)
        {
            if (!snap.EasyTierRunning) return "danger";
            if (snap.SummaryMode == "P2P") return "success";
            if (snap.SummaryMode == "Relay") return "warning";
            return "accent";
        }

        public int RunSelfTestAndCapture(string outputDir, bool demoMode = false)
        {
            try
            {
                Directory.CreateDirectory(outputDir);
                _initialRefreshDone = true;
                if (_refreshTimer != null) _refreshTimer.Stop();
                if (demoMode)
                {
                    _snapshot = CreateDemoSnapshot();
                }
                else
                {
                    _snapshot = _backend.CollectSnapshot(true);
                }

                Show();
                WindowState = WindowState.Normal;
                Width = 980;
                Height = 680;

                // Light theme captures
                ApplyThemeAndRebuild(false);
                CapturePageToPng(0, Path.Combine(outputDir, "page1_home_light.png"), false);
                CapturePageToPng(1, Path.Combine(outputDir, "page2_network_light.png"), false);
                CapturePageToPng(1, Path.Combine(outputDir, "page2_network_bottom_light.png"), true);
                CapturePageToPng(2, Path.Combine(outputDir, "page3_pairing_light.png"), false);
                CapturePageToPng(2, Path.Combine(outputDir, "page3_pairing_bottom_light.png"), true);
                CapturePageToPng(3, Path.Combine(outputDir, "page4_settings_light.png"), false);

                // Min-width 860x560 layout check (revealed secret + host detail expanded)
                Width = 860;
                Height = 560;
                _showNetworkSecret = true;
                _showHostDetail = true;
                CapturePageToPng(0, Path.Combine(outputDir, "page1_home_minwidth_light.png"), false);
                _showNetworkSecret = false;
                _showHostDetail = false;
                Width = 980;
                Height = 680;

                // Dark theme captures
                ApplyThemeAndRebuild(true);
                CapturePageToPng(0, Path.Combine(outputDir, "page1_home_dark.png"), false);
                CapturePageToPng(2, Path.Combine(outputDir, "page3_pairing_dark.png"), false);

                ApplyThemeAndRebuild(string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase));

                string qrText = BackendService.BuildPairingSummaryText(_snapshot.Config);
                BitmapSource qrBmp = QrCodeEncoder.RenderBitmap(qrText, 6, 4);
                string qrPath = Path.Combine(outputDir, "pairing_qr.png");
                QrCodeEncoder.SavePng(qrBmp, qrPath);

                Dictionary<string, object> report = new Dictionary<string, object>();
                report["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                report["is_admin"] = _snapshot.IsAdmin;
                report["workspace_root"] = _snapshot.WorkspaceRoot;
                report["config_exists"] = _snapshot.Config.Exists;
                report["host_ip"] = _snapshot.Config.HostIpOnly;
                report["no_tun_config_ok"] = _snapshot.NoTunConfigOk;
                report["no_tun_adapter_ok"] = _snapshot.NoTunAdapterOk;
                report["easytier_running"] = _snapshot.EasyTierRunning;
                report["easytier_version"] = _snapshot.EasyTierVersion ?? "";
                report["apollo_installed"] = _snapshot.ApolloInstalled;
                report["apollo_running"] = _snapshot.ApolloRunning;
                report["apollo_listening_47990"] = _snapshot.ApolloListening47990;
                report["apollo_service"] = _snapshot.ApolloServiceName ?? "";
                report["apollo_exe"] = _snapshot.ApolloExePath ?? "";
                report["firewall_passed"] = _snapshot.Firewall.PassedCount;
                report["firewall_total"] = _snapshot.Firewall.TotalCount;
                report["easytier_autostart"] = _snapshot.EasyTierAutostart;
                report["ui_autostart"] = _snapshot.UiAutostart;
                report["elevated_task_ready"] = _snapshot.ElevatedTaskReady;
                report["ac_never_sleep"] = _snapshot.AcNeverSleep;
                report["display_adapter_count"] = _snapshot.DisplayAdapters.Count;
                report["qr_payload_length"] = qrText.Length;

                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(Path.Combine(outputDir, "ui_selftest_result.json"), ser.Serialize(report), new System.Text.UTF8Encoding(false));
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(outputDir, "ui_selftest_error.txt"), ex.ToString(), new System.Text.UTF8Encoding(false));
                }
                catch { }
                return 1;
            }
        }

        private void CapturePageToPng(int pageIdx, string filePath, bool scrollToBottom)
        {
            _currentPageIndex = pageIdx;
            RenderPageContent(pageIdx, false);
            UpdateLayout();
            Dispatcher.Invoke((Action)(() => { }), DispatcherPriority.Render);
            int w = Math.Max(860, (int)Math.Ceiling(ActualWidth > 0 ? ActualWidth : Width));
            int h = Math.Max(560, (int)Math.Ceiling(ActualHeight > 0 ? ActualHeight : Height));
            _rootGrid.Measure(new Size(w, h));
            _rootGrid.Arrange(new Rect(0, 0, w, h));
            _rootGrid.UpdateLayout();

            if (scrollToBottom && _contentScroll != null)
            {
                _contentScroll.ScrollToEnd();
                _rootGrid.UpdateLayout();
                Dispatcher.Invoke((Action)(() => { }), DispatcherPriority.Render);
            }

            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_rootGrid);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                enc.Save(fs);
            }
        }

        private static RuntimeSnapshot CreateDemoSnapshot()
        {
            RuntimeSnapshot s = new RuntimeSnapshot();
            s.Timestamp = DateTime.Now;
            s.IsAdmin = true;
            s.WorkspaceRoot = @"D:\远程控制";
            s.EasyTierInstalled = true;
            s.EasyTierRunning = true;
            s.EasyTierVersion = "2.6.4";
            s.EasyTierPids = "8420";
            s.EasyTierHostname = "Workstation-PC";
            s.NoTunConfigOk = true;
            s.NoTunAdapterOk = true;
            s.ApolloInstalled = true;
            s.ApolloRunning = true;
            s.ApolloListening47990 = true;
            s.ApolloServiceName = "ApolloService";
            s.ApolloExePath = @"D:\Apollo\sunshine.exe";
            s.EasyTierAutostart = true;
            s.UiAutostart = true;
            s.ElevatedTaskReady = true;
            s.AcNeverSleep = true;
            s.SummaryMode = "P2P";
            s.SummaryLatency = "10.8 ms";

            s.Config = new EasyTierConfig
            {
                Exists = true,
                Ipv4 = "10.126.126.1/24",
                Dhcp = false,
                NetworkName = "rc-demo2026",
                NetworkSecret = "DemoSecret1234567890",
                NoTun = true,
                EnableEncryption = true,
                EnableIpv6 = true,
                Peers = new List<string>
                {
                    "tcp://225284.xyz:11010",
                    "tcp://easytier.weiai.org.cn:11010",
                    "tcp://183.230.36.171:11010"
                }
            };

            s.Peers.Add(new PeerNodeInfo
            {
                Hostname = "Workstation-PC",
                VirtualIp = "10.126.126.1/24",
                CostMode = "Local",
                LatencyText = "-",
                LatencyMs = -1,
                LossRate = "-",
                TunnelProto = "-",
                Version = "2.6.4",
                IsLocal = true
            });
            s.Peers.Add(new PeerNodeInfo
            {
                Hostname = "Xiaomi-Pad-Pro",
                VirtualIp = "10.126.126.2/24",
                CostMode = "p2p",
                LatencyText = "10.8 ms",
                LatencyMs = 10.8,
                LossRate = "0.0%",
                TunnelProto = "tcp",
                Version = "2.6.4",
                IsLocal = false,
                IsPublicServer = false
            });
            s.Peers.Add(new PeerNodeInfo
            {
                Hostname = "PublicServer_CN_1",
                VirtualIp = "",
                CostMode = "p2p",
                LatencyText = "38.2 ms",
                LatencyMs = 38.2,
                LossRate = "0.0%",
                TunnelProto = "tcp",
                Version = "2.6.4",
                IsLocal = false,
                IsPublicServer = true
            });

            s.Firewall = new FirewallAuditResult
            {
                TotalCount = 5,
                PassedCount = 5
            };
            s.DisplayAdapters.Add(new DisplayAdapterInfo { Name = "SudoMaker Virtual Display Adapter", Category = "ApolloVDA", Status = "OK" });
            s.DisplayAdapters.Add(new DisplayAdapterInfo { Name = "NVIDIA GeForce RTX GPU", Category = "Physical", Status = "OK" });
            return s;
        }
    }
}
