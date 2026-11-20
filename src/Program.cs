using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("远程控制中心")]
[assembly: AssemblyDescription("Windows 远程控制管理客户端 (Apollo + EasyTier)")]
[assembly: AssemblyProduct("远程控制中心")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]

namespace RemoteControlUI
{
    public static class Program
    {
        public const string MutexName = "Local\\RemoteControlUI_SingleInstance_v2";
        public const string ActivateEventName = "Local\\RemoteControlUI_ActivateEvent_v2";

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
            string StringSecurityDescriptor,
            uint StringSDRevision,
            out IntPtr SecurityDescriptor,
            out uint SecurityDescriptorSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeWaitHandle CreateEventW(
            ref SECURITY_ATTRIBUTES lpEventAttributes,
            bool bManualReset,
            bool bInitialState,
            string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private static EventWaitHandle CreateCrossIlActivateEvent()
        {
            // Grant SYNCHRONIZE | EVENT_MODIFY_STATE (0x00100002) to current user SID
            // with Medium Mandatory Integrity Label so a Medium-IL desktop launcher can
            // restore an elevated High-IL tray instance with zero UAC prompt.
            IntPtr sdPtr = IntPtr.Zero;
            try
            {
                string userSid = WindowsIdentity.GetCurrent().User.Value;
                string sddl = string.Format(
                    "D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x00100002;;;{0})S:(ML;;NW;;;ME)",
                    userSid);
                uint sdSize;
                if (ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out sdPtr, out sdSize) && sdPtr != IntPtr.Zero)
                {
                    SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES
                    {
                        nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                        lpSecurityDescriptor = sdPtr,
                        bInheritHandle = 0
                    };
                    SafeWaitHandle h = CreateEventW(ref sa, false, false, ActivateEventName);
                    if (h != null && !h.IsInvalid)
                    {
                        EventWaitHandle ewh = new EventWaitHandle(false, EventResetMode.AutoReset);
                        ewh.SafeWaitHandle.Close();
                        ewh.SafeWaitHandle = h;
                        return ewh;
                    }
                }
            }
            catch { }
            finally
            {
                if (sdPtr != IntPtr.Zero) LocalFree(sdPtr);
            }
            return new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            bool startMinimized = false;
            string selfTestDir = null;
            bool demoSelfTest = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? "").Trim();
                if (string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/minimized", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;
                }
                else if (string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    selfTestDir = args[i + 1];
                    demoSelfTest = false;
                    i++;
                }
                else if (string.Equals(a, "--demo-self-test", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    selfTestDir = args[i + 1];
                    demoSelfTest = true;
                    i++;
                }
            }

            if (!string.IsNullOrEmpty(selfTestDir))
            {
                Application testApp = new Application();
                testApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BackendService backend = new BackendService();
                UiSettings settings = UiSettings.Load(backend.SettingsPath);
                MainWindow win = new MainWindow(backend, settings, false);
                int exitCode = win.RunSelfTestAndCapture(selfTestDir, demoSelfTest);
                win.ExitApplication();
                return exitCode;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(ActivateEventName))
                        {
                            ev.Set();
                        }
                    }
                    catch { }
                    return 0;
                }

                EventWaitHandle activateEvent = CreateCrossIlActivateEvent();
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                BackendService backend = new BackendService();
                UiSettings settings = UiSettings.Load(backend.SettingsPath);
                MainWindow mainWin = new MainWindow(backend, settings, startMinimized);
                app.MainWindow = mainWin;

                int exiting = 0;
                Thread waitThread = new Thread(() =>
                {
                    try
                    {
                        while (Volatile.Read(ref exiting) == 0)
                        {
                            activateEvent.WaitOne();
                            if (Volatile.Read(ref exiting) != 0) break;
                            if (app.Dispatcher != null && !app.Dispatcher.HasShutdownStarted && !app.Dispatcher.HasShutdownFinished)
                            {
                                app.Dispatcher.BeginInvoke((Action)(() => mainWin.RestoreFromTray()));
                            }
                        }
                    }
                    catch { }
                });
                waitThread.IsBackground = true;
                waitThread.Start();

                if (!startMinimized)
                {
                    mainWin.Show();
                }

                // Call app.Run() without passing mainWin so --minimized never flashes the window!
                app.Run();

                Interlocked.Exchange(ref exiting, 1);
                try { activateEvent.Set(); } catch { }
                try { activateEvent.Dispose(); } catch { }
                return 0;
            }
        }
    }
}
