using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Threading;

[assembly: AssemblyTitle("远程控制中心启动器")]
[assembly: AssemblyDescription("唤醒托盘实例或启动远程控制中心")]
[assembly: AssemblyProduct("远程控制中心")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]

namespace RemoteControlLauncher
{
    internal static class Program
    {
        private const string ActivateEventName = "Local\\RemoteControlUI_ActivateEvent_v2";
        private const string ElevatedTaskName = "RemoteControlUI-ElevatedLaunch";

        [STAThread]
        private static int Main(string[] args)
        {
            // 1. Instant zero-UAC restore if RemoteControlUI is already running in system tray
            if (TrySignalRunningInstance())
            {
                return 0;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string uiExe = Path.Combine(baseDir, "RemoteControlUI.exe");

            // 2. Try zero-UAC elevated launch via Scheduled Task (only if no custom args passed)
            if (args.Length == 0 && TryRunElevatedTask())
            {
                // Verify process actually started or event became available
                for (int i = 0; i < 12; i++)
                {
                    Thread.Sleep(250);
                    if (IsUiRunning()) return 0;
                }
            }

            // 3. Guaranteed fallback: launch RemoteControlUI.exe directly (triggers UAC cleanly)
            if (File.Exists(uiExe))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = uiExe,
                        WorkingDirectory = baseDir,
                        UseShellExecute = true
                    };
                    if (args.Length > 0)
                    {
                        psi.Arguments = string.Join(" ", args);
                    }
                    Process.Start(psi);
                    return 0;
                }
                catch
                {
                    return 1;
                }
            }
            return 2;
        }

        private static bool TrySignalRunningInstance()
        {
            try
            {
                using (EventWaitHandle ev = EventWaitHandle.OpenExisting(
                    ActivateEventName,
                    EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize))
                {
                    ev.Set();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryRunElevatedTask()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/run /tn \"" + ElevatedTaskName + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (!p.WaitForExit(4000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUiRunning()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("RemoteControlUI");
                try { return ps.Length > 0; }
                finally
                {
                    for (int i = 0; i < ps.Length; i++) { try { ps[i].Dispose(); } catch { } }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
