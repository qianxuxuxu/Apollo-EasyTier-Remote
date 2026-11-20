using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RemoteControlUI
{
    public static class UiIcons
    {
        // Crisp 24x24 geometric vector glyphs
        public const string DevicesGroup = "M3 5H15V14H3ZM7 18H11M9 14V18M17 8H21V18H17ZM19 16H19.01";
        public const string AllDevices   = "M4 4H10V10H4ZM14 4H20V10H14ZM4 14H10V20H4ZM14 14H20V20H14Z";
        public const string LinkGroup    = "M4 4H10V10H4ZM14 14H20V20H14ZM10 7H17V14M7 14V17H14";
        public const string QrPairing    = "M4 4H10V10H4ZM14 4H20V10H14ZM4 14H10V20H4ZM15 15H17V17H15ZM18 18H20V20H18ZM15 19H17V20H15ZM18 14H20V16H18Z";
        public const string Network      = "M12 2A10 10 0 1 0 22 12A10 10 0 0 0 12 2ZM2 12H22M12 2A15.3 15.3 0 0 1 16 12A15.3 15.3 0 0 1 12 22A15.3 15.3 0 0 1 8 12A15.3 15.3 0 0 1 12 2Z";
        public const string Settings     = "M12 15A3 3 0 1 0 12 9A3 3 0 0 0 12 15ZM19.4 15A1.65 1.65 0 0 0 19.73 16.82L19.79 16.88A2 2 0 0 1 16.96 19.71L16.9 19.65A1.65 1.65 0 0 0 15.08 19.32A1.65 1.65 0 0 0 14.08 20.83V21A2 2 0 0 1 10.08 21V20.91A1.65 1.65 0 0 0 9 19.4A1.65 1.65 0 0 0 7.18 19.73L7.12 19.79A2 2 0 0 1 4.29 16.96L4.35 16.9A1.65 1.65 0 0 0 4.68 15.08A1.65 1.65 0 0 0 3.17 14.08H3A2 2 0 0 1 3 10.08H3.09A1.65 1.65 0 0 0 4.6 9A1.65 1.65 0 0 0 4.27 7.18L4.21 7.12A2 2 0 0 1 7.04 4.29L7.1 4.35A1.65 1.65 0 0 0 8.92 4.68H9A1.65 1.65 0 0 0 10 3.17V3A2 2 0 0 1 14 3V3.09A1.65 1.65 0 0 0 15 4.6A1.65 1.65 0 0 0 16.82 4.27L16.88 4.21A2 2 0 0 1 19.71 7.04L19.65 7.1A1.65 1.65 0 0 0 19.32 8.92V9A1.65 1.65 0 0 0 20.83 10H21A2 2 0 0 1 21 14H20.91A1.65 1.65 0 0 0 19.4 15Z";
        public const string Refresh      = "M20 12A8 8 0 1 1 17.65 6.35M20 4V9H15";
        public const string Sun          = "M12 16.5A4.5 4.5 0 1 0 12 7.5A4.5 4.5 0 0 0 12 16.5ZM12 2V4M12 20V22M4.93 4.93L6.34 6.34M17.66 17.66L19.07 19.07M2 12H4M20 12H22M4.93 19.07L6.34 17.66M17.66 6.34L19.07 4.93";
        public const string Moon         = "M20.5 13A8.5 8.5 0 1 1 11 3.5A6.8 6.8 0 0 0 20.5 13Z";
        public const string External     = "M18 13V19A2 2 0 0 1 16 21H5A2 2 0 0 1 3 19V8A2 2 0 0 1 5 6H11M15 3H21V9M10 14L21 3";
        public const string InfoCircle   = "M12 2A10 10 0 1 0 22 12A10 10 0 0 0 12 2ZM12 11V16M12 8H12.01";
        public const string ChevronDown  = "M6 9L12 15L18 9";
        public const string ChevronUp    = "M6 15L12 9L18 15";
        public const string ChevronRight = "M9 6L15 12L9 18";
        public const string Tablet       = "M6 3H18A2 2 0 0 1 20 5V19A2 2 0 0 1 18 21H6A2 2 0 0 1 4 19V5A2 2 0 0 1 6 3ZM12 17.5H12.01";
        public const string Monitor      = "M3 4H21V16H3ZM8 20H16M12 16V20";
        public const string WinMinimize  = "M5 12H19";
        public const string WinMaximize  = "M5.5 5.5H18.5V18.5H5.5Z";
        public const string WinRestore   = "M8 5.5H18.5V16H16M5.5 8H16V18.5H5.5Z";
        public const string WinClose     = "M6 6L18 18M18 6L6 18";

        public static UIElement Create(string pathData, double size, Brush stroke, double strokeThickness)
        {
            Viewbox vb = new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform
            };
            Canvas canvas = new Canvas { Width = 24, Height = 24 };
            System.Windows.Shapes.Path p = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(pathData),
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent
            };
            canvas.Children.Add(p);
            vb.Child = canvas;
            return vb;
        }

        public static UIElement CreateFilledBrandMark(double size)
        {
            // Minimalist clean blue geometric mark (similar to UU's crisp corner mark)
            Viewbox vb = new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform };
            Canvas c = new Canvas { Width = 24, Height = 24 };

            System.Windows.Shapes.Path p1 = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M3 5L11 12L3 19Z"),
                Fill = new SolidColorBrush(Color.FromRgb(22, 119, 255))
            };
            System.Windows.Shapes.Path p2 = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M11 3L21 12L11 21L13 12Z"),
                Fill = new SolidColorBrush(Color.FromRgb(56, 189, 248))
            };
            c.Children.Add(p2);
            c.Children.Add(p1);
            vb.Child = c;
            return vb;
        }

        public static UIElement CreateWindowsSquareBadge(double boxSize = 36)
        {
            Border b = new Border
            {
                Width = boxSize,
                Height = boxSize,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(22, 119, 255))
            };
            // Classic 4-pane clean Windows icon inside blue square (just like UU Remote!)
            Grid g = new Grid { Width = 17, Height = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            g.RowDefinitions.Add(new RowDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
            g.ColumnDefinitions.Add(new ColumnDefinition());

            int[,] cells = new int[,] { { 0, 0 }, { 0, 2 }, { 2, 0 }, { 2, 2 } };
            for (int i = 0; i < 4; i++)
            {
                Border pane = new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(1.2)
                };
                Grid.SetRow(pane, cells[i, 0]);
                Grid.SetColumn(pane, cells[i, 1]);
                g.Children.Add(pane);
            }
            b.Child = g;
            return b;
        }

        public static UIElement CreateTabletSquareBadge(double boxSize = 36)
        {
            Border b = new Border
            {
                Width = boxSize,
                Height = boxSize,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(22, 119, 255))
            };
            b.Child = Create(Tablet, 19, Brushes.White, 2.0);
            return b;
        }
    }

    public static class UiFactory
    {
        public static FontFamily SansFont = new FontFamily("Microsoft YaHei UI, Segoe UI, sans-serif");
        public static FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas, Courier New, monospace");

        public static Border CreateCard(UIElement child, Thickness? padding = null, Thickness? margin = null)
        {
            Border b = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = padding ?? new Thickness(16, 14, 16, 14),
                Margin = margin ?? new Thickness(0, 0, 0, 12),
                Child = child
            };
            b.SetResourceReference(Border.BackgroundProperty, "Brush.CardBg");
            b.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            return b;
        }

        public static TextBlock CreatePageHeading(string text)
        {
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontFamily = SansFont,
                FontSize = 21,
                FontWeight = FontWeights.Bold
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            return tb;
        }

        public static TextBlock CreateTitle(string text, double size = 14)
        {
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontFamily = SansFont,
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            return tb;
        }

        public static TextBlock CreateSubtle(string text, double size = 12, bool wrap = true)
        {
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontFamily = SansFont,
                FontSize = size,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            return tb;
        }

        public static UIElement CreateGroupHeader(string label, bool expanded = true, Action onToggle = null)
        {
            StackPanel sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = onToggle != null ? Cursors.Hand : Cursors.Arrow
            };
            UIElement chev = UiIcons.Create(expanded ? UiIcons.ChevronDown : UiIcons.ChevronRight, 13, new SolidColorBrush(ThemeManager.TextMuted), 1.8);
            Border chevWrap = new Border { Margin = new Thickness(0, 1, 6, 0), Child = chev };
            TextBlock tb = new TextBlock
            {
                Text = label,
                FontFamily = SansFont,
                FontSize = 12.5,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            sp.Children.Add(chevWrap);
            sp.Children.Add(tb);
            if (onToggle != null)
            {
                sp.Background = Brushes.Transparent;
                sp.MouseEnter += (s, e) => tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
                sp.MouseLeave += (s, e) => tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
                sp.MouseLeftButtonUp += (s, e) => onToggle();
            }
            return sp;
        }

        public static Border CreateTag(string text, string tone)
        {
            // Flat clean pill tag (like UU's "本机" tag, no noisy dot)
            string fgKey = "Brush.TextSecondary";
            string bgKey = "Brush.SubtleBg";
            if (tone == "success") { fgKey = "Brush.Success"; bgKey = "Brush.SuccessSoftBg"; }
            else if (tone == "warning") { fgKey = "Brush.Warning"; bgKey = "Brush.WarningSoftBg"; }
            else if (tone == "danger") { fgKey = "Brush.Danger"; bgKey = "Brush.DangerSoftBg"; }
            else if (tone == "accent") { fgKey = "Brush.Accent"; bgKey = "Brush.AccentSoftBg"; }

            TextBlock tb = new TextBlock
            {
                Text = text,
                FontFamily = SansFont,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, fgKey);

            Border pill = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = tb
            };
            pill.SetResourceReference(Border.BackgroundProperty, bgKey);
            return pill;
        }

        public static StackPanel CreateStatusInline(string text, string tone)
        {
            string fgKey = "Brush.TextMuted";
            if (tone == "success") fgKey = "Brush.Success";
            else if (tone == "warning") fgKey = "Brush.Warning";
            else if (tone == "danger") fgKey = "Brush.Danger";
            else if (tone == "accent") fgKey = "Brush.Accent";

            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Ellipse dot = new Ellipse
            {
                Width = 6.5,
                Height = 6.5,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            dot.SetResourceReference(Shape.FillProperty, fgKey);
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontFamily = SansFont,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, tone == "neutral" ? "Brush.TextMuted" : fgKey);
            sp.Children.Add(dot);
            sp.Children.Add(tb);
            return sp;
        }

        public static Button CreateButton(string text, string variant, RoutedEventHandler onClick, double height = 30, double minWidth = 0)
        {
            // variant: "primary", "secondary", "danger", "ghost", "link"
            Button btn = new Button
            {
                Content = text,
                Height = height,
                MinWidth = minWidth,
                Padding = variant == "link" ? new Thickness(6, 0, 6, 0) : new Thickness(12, 0, 12, 0),
                FontFamily = SansFont,
                FontSize = 12.2,
                FontWeight = variant == "primary" ? FontWeights.Medium : FontWeights.Normal,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(variant == "ghost" || variant == "link" ? 0 : 1),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            Brush normalBg;
            Brush hoverBg;
            Brush fg;
            Brush borderBrush;

            if (variant == "primary")
            {
                normalBg = new SolidColorBrush(ThemeManager.Accent);
                hoverBg = new SolidColorBrush(ThemeManager.AccentHover);
                fg = Brushes.White;
                borderBrush = normalBg;
            }
            else if (variant == "danger")
            {
                normalBg = new SolidColorBrush(ThemeManager.CardBg);
                hoverBg = new SolidColorBrush(ThemeManager.DangerSoftBg);
                fg = new SolidColorBrush(ThemeManager.Danger);
                borderBrush = new SolidColorBrush(ThemeManager.IsDark ? Color.FromRgb(127, 29, 29) : Color.FromRgb(253, 186, 186));
            }
            else if (variant == "link")
            {
                normalBg = Brushes.Transparent;
                hoverBg = new SolidColorBrush(ThemeManager.AccentSoftBg);
                fg = new SolidColorBrush(ThemeManager.Accent);
                borderBrush = Brushes.Transparent;
            }
            else if (variant == "ghost")
            {
                normalBg = Brushes.Transparent;
                hoverBg = new SolidColorBrush(ThemeManager.SubtleBg);
                fg = new SolidColorBrush(ThemeManager.TextSecondary);
                borderBrush = Brushes.Transparent;
            }
            else
            {
                normalBg = new SolidColorBrush(ThemeManager.CardBg);
                hoverBg = new SolidColorBrush(ThemeManager.SubtleBg);
                fg = new SolidColorBrush(ThemeManager.TextPrimary);
                borderBrush = new SolidColorBrush(ThemeManager.BorderColor);
            }

            btn.Background = normalBg;
            btn.Foreground = fg;
            btn.BorderBrush = borderBrush;

            ControlTemplate tpl = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            Trigger hoverTr = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTr.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "bd"));
            tpl.Triggers.Add(hoverTr);

            Trigger disabledTr = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTr.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
            tpl.Triggers.Add(disabledTr);

            btn.Template = tpl;
            if (onClick != null) btn.Click += onClick;
            return btn;
        }

        public static Button CreateIconButton(string pathData, string tooltip, RoutedEventHandler onClick, double boxSize = 30, double iconSize = 15)
        {
            Button btn = new Button
            {
                Content = UiIcons.Create(pathData, iconSize, new SolidColorBrush(ThemeManager.TextSecondary), 1.65),
                ToolTip = tooltip,
                Width = boxSize,
                Height = boxSize,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };

            ControlTemplate tpl = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            Trigger hoverTr = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTr.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(ThemeManager.NavHoverBg), "bd"));
            tpl.Triggers.Add(hoverTr);

            btn.Template = tpl;
            if (onClick != null) btn.Click += onClick;
            return btn;
        }

        public static Border CreateToggleSwitch(bool isOn, Action<bool> onChanged)
        {
            bool state = isOn;
            Border track = new Border
            {
                Width = 38,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Cursor = Cursors.Hand,
                Padding = new Thickness(2.5),
                VerticalAlignment = VerticalAlignment.Center
            };
            Border knob = new Border
            {
                Width = 15,
                Height = 15,
                CornerRadius = new CornerRadius(7.5),
                Background = Brushes.White,
                HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };
            track.Child = knob;
            track.Background = state
                ? new SolidColorBrush(ThemeManager.Accent)
                : new SolidColorBrush(ThemeManager.IsDark ? Color.FromRgb(71, 85, 105) : Color.FromRgb(203, 213, 225));

            track.MouseLeftButtonUp += (s, e) =>
            {
                state = !state;
                knob.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                track.Background = state
                    ? new SolidColorBrush(ThemeManager.Accent)
                    : new SolidColorBrush(ThemeManager.IsDark ? Color.FromRgb(71, 85, 105) : Color.FromRgb(203, 213, 225));
                if (onChanged != null) onChanged(state);
            };
            return track;
        }

        public static TextBox CreateTextBox(string initialText, double height = 32, bool readOnly = false)
        {
            TextBox tb = new TextBox
            {
                Text = initialText ?? "",
                Height = height,
                IsReadOnly = readOnly,
                FontFamily = SansFont,
                FontSize = 12.5,
                Padding = new Thickness(10, 4, 10, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(1)
            };
            tb.SetResourceReference(TextBox.BackgroundProperty, "Brush.InputBg");
            tb.SetResourceReference(TextBox.ForegroundProperty, "Brush.TextPrimary");
            tb.SetResourceReference(TextBox.BorderBrushProperty, "Brush.Border");
            return tb;
        }

        public static UIElement CreatePlaceholderTextBox(string initialText, string placeholder, Action<string> onTextChanged, Action onEnter, out TextBox innerBox, double height = 32)
        {
            Grid host = new Grid { Height = height };
            TextBox tb = CreateTextBox(initialText, height, false);
            TextBlock ph = new TextBlock
            {
                Text = placeholder ?? "",
                FontFamily = SansFont,
                FontSize = 12.2,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 10, 0),
                IsHitTestVisible = false,
                Visibility = string.IsNullOrEmpty(initialText) ? Visibility.Visible : Visibility.Collapsed
            };
            ph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

            tb.TextChanged += (s, e) =>
            {
                string val = tb.Text ?? "";
                ph.Visibility = string.IsNullOrEmpty(val) ? Visibility.Visible : Visibility.Collapsed;
                if (onTextChanged != null) onTextChanged(val);
            };
            if (onEnter != null)
            {
                tb.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        onEnter();
                        e.Handled = true;
                    }
                };
            }

            host.Children.Add(tb);
            host.Children.Add(ph);
            innerBox = tb;
            return host;
        }

        public static Border CreateDivider(Thickness? margin = null)
        {
            Border line = new Border
            {
                Height = 1,
                Margin = margin ?? new Thickness(0)
            };
            line.SetResourceReference(Border.BackgroundProperty, "Brush.Divider");
            return line;
        }
    }
}
