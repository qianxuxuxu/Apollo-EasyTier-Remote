using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RemoteControlUI
{
    public static class ThemeManager
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static bool IsDark { get; private set; }

        public static Color ShellBg { get; private set; }
        public static Color CanvasBg { get; private set; }
        public static Color SidebarBg { get; private set; }
        public static Color ContentBg { get; private set; }
        public static Color CardBg { get; private set; }
        public static Color SubtleBg { get; private set; }
        public static Color NavActiveBg { get; private set; }
        public static Color NavHoverBg { get; private set; }
        public static Color InputBg { get; private set; }
        public static Color BorderColor { get; private set; }
        public static Color DividerColor { get; private set; }
        public static Color TextPrimary { get; private set; }
        public static Color TextSecondary { get; private set; }
        public static Color TextMuted { get; private set; }
        public static Color Accent { get; private set; }
        public static Color AccentHover { get; private set; }
        public static Color AccentSoftBg { get; private set; }
        public static Color Success { get; private set; }
        public static Color SuccessSoftBg { get; private set; }
        public static Color Warning { get; private set; }
        public static Color WarningSoftBg { get; private set; }
        public static Color Danger { get; private set; }
        public static Color DangerSoftBg { get; private set; }
        public static Color CodeBg { get; private set; }

        public static void ApplyTheme(bool dark, Window targetWindow)
        {
            IsDark = dark;
            if (!dark)
            {
                // Clean minimalist desktop palette (UU Remote inspired)
                ShellBg       = Hex("#F5F6F8");
                SidebarBg     = Hex("#F5F6F8");
                CanvasBg      = Hex("#F5F6F8");
                ContentBg     = Hex("#FFFFFF");
                CardBg        = Hex("#FFFFFF");
                SubtleBg      = Hex("#F7F8FA");
                NavActiveBg   = Hex("#E6E9EF");
                NavHoverBg    = Hex("#ECEFF4");
                InputBg       = Hex("#FFFFFF");
                BorderColor   = Hex("#E6E8EE");
                DividerColor  = Hex("#F0F2F5");
                TextPrimary   = Hex("#1D2129");
                TextSecondary = Hex("#4E5969");
                TextMuted     = Hex("#86909C");
                Accent        = Hex("#1677FF");
                AccentHover   = Hex("#0958D9");
                AccentSoftBg  = Hex("#E8F3FF");
                Success       = Hex("#00A854");
                SuccessSoftBg = Hex("#E8FFEA");
                Warning       = Hex("#D97706");
                WarningSoftBg = Hex("#FFF7E6");
                Danger        = Hex("#F53F3F");
                DangerSoftBg  = Hex("#FFECE8");
                CodeBg        = Hex("#F7F8FA");
            }
            else
            {
                ShellBg       = Hex("#16191F");
                SidebarBg     = Hex("#16191F");
                CanvasBg      = Hex("#16191F");
                ContentBg     = Hex("#1E222A");
                CardBg        = Hex("#232832");
                SubtleBg      = Hex("#282E3A");
                NavActiveBg   = Hex("#2A303D");
                NavHoverBg    = Hex("#222732");
                InputBg       = Hex("#1A1E26");
                BorderColor   = Hex("#313846");
                DividerColor  = Hex("#2A303C");
                TextPrimary   = Hex("#F2F3F5");
                TextSecondary = Hex("#C9CDD4");
                TextMuted     = Hex("#86909C");
                Accent        = Hex("#3B82F6");
                AccentHover   = Hex("#60A5FA");
                AccentSoftBg  = Hex("#1D2D4D");
                Success       = Hex("#34D399");
                SuccessSoftBg = Hex("#0F2E26");
                Warning       = Hex("#FBBF24");
                WarningSoftBg = Hex("#332510");
                Danger        = Hex("#F87171");
                DangerSoftBg  = Hex("#3B1A1D");
                CodeBg        = Hex("#1A1E26");
            }

            if (Application.Current != null)
            {
                ResourceDictionary res = Application.Current.Resources;
                SetBrush(res, "Brush.ShellBg", ShellBg);
                SetBrush(res, "Brush.CanvasBg", CanvasBg);
                SetBrush(res, "Brush.SidebarBg", SidebarBg);
                SetBrush(res, "Brush.ContentBg", ContentBg);
                SetBrush(res, "Brush.CardBg", CardBg);
                SetBrush(res, "Brush.SubtleBg", SubtleBg);
                SetBrush(res, "Brush.NavActiveBg", NavActiveBg);
                SetBrush(res, "Brush.NavHoverBg", NavHoverBg);
                SetBrush(res, "Brush.InputBg", InputBg);
                SetBrush(res, "Brush.Border", BorderColor);
                SetBrush(res, "Brush.Divider", DividerColor);
                SetBrush(res, "Brush.TextPrimary", TextPrimary);
                SetBrush(res, "Brush.TextSecondary", TextSecondary);
                SetBrush(res, "Brush.TextMuted", TextMuted);
                SetBrush(res, "Brush.Accent", Accent);
                SetBrush(res, "Brush.AccentHover", AccentHover);
                SetBrush(res, "Brush.AccentSoftBg", AccentSoftBg);
                SetBrush(res, "Brush.Success", Success);
                SetBrush(res, "Brush.SuccessSoftBg", SuccessSoftBg);
                SetBrush(res, "Brush.Warning", Warning);
                SetBrush(res, "Brush.WarningSoftBg", WarningSoftBg);
                SetBrush(res, "Brush.Danger", Danger);
                SetBrush(res, "Brush.DangerSoftBg", DangerSoftBg);
                SetBrush(res, "Brush.CodeBg", CodeBg);
            }

            if (targetWindow != null)
            {
                ApplyDwmWindowStyle(targetWindow, dark);
            }
        }

        public static void ApplyDwmWindowStyle(Window win, bool dark)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd == IntPtr.Zero) return;
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                int darkVal = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));
            }
            catch { }
        }

        private static void SetBrush(ResourceDictionary res, string key, Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            res[key] = b;
        }

        public static SolidColorBrush Brush(string key)
        {
            if (Application.Current != null && Application.Current.Resources.Contains(key))
            {
                return Application.Current.Resources[key] as SolidColorBrush;
            }
            return Brushes.Transparent;
        }

        public static Color Hex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}
