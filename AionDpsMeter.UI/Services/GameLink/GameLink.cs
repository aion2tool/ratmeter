using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AionDpsMeter.Services.Services.Settings;
using Serilog;

namespace AionDpsMeter.UI.Services.GameLink
{
    /// <summary>
    /// Ties the meter to the game:
    /// - Start with AION 2: a shortcut in the Windows Startup folder runs this exe with --watch at logon. That
    ///   copy opens no window; it checks every few seconds for the game window and starts the meter once per
    ///   game session (closing the meter yourself keeps it closed until the next time the game starts).
    /// - Close with AION 2: the meter closes itself (saving fights to History) once the game window has been
    ///   gone for a short while, if it saw the game running first.
    /// </summary>
    public static class GameLink
    {
        public const string WatchArgument = "--watch";
        private const string MeterMutexName = @"Local\AION2DpsMeter.SingleInstance";
        private const string WatcherMutexName = @"Local\AION2DpsMeter.Watcher";
        private static readonly TimeSpan WatchEvery = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan CloseAfterGameGone = TimeSpan.FromSeconds(30);

        private static string ShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "AION2 DPS Meter (starts with the game).lnk");

        private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AionDpsMeter.UI.exe");

        /// <summary>The AION 2 client window is open (the launcher / helper process has no "AION2" window).</summary>
        public static bool IsGameRunning()
        {
            foreach (var p in Process.GetProcessesByName("AION2"))
            {
                try { if (p.MainWindowTitle.StartsWith("AION2", StringComparison.Ordinal)) return true; }
                catch (Exception) { /* exited meanwhile */ }
                finally { p.Dispose(); }
            }
            return false;
        }

        private static bool MeterRunning() => Mutex.TryOpenExisting(MeterMutexName, out var m) && Dispose(m);
        private static bool Dispose(Mutex m) { m.Dispose(); return true; }

        // ------------------------------------------------------------------ start with the game

        public static bool IsAutoStartEnabled => File.Exists(ShortcutPath);

        /// <summary>Creates / removes the Startup shortcut; enabling also starts the watcher right away.</summary>
        public static void SetAutoStart(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                    return;
                }
                var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell not available");
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic link = shell.CreateShortcut(ShortcutPath);
                link.TargetPath = ExePath;
                link.Arguments = WatchArgument;
                link.WorkingDirectory = Path.GetDirectoryName(ExePath);
                link.Description = "Starts the AION 2 DPS meter when the game starts";
                link.Save();
                Process.Start(new ProcessStartInfo(ExePath, WatchArgument) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExePath) });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Start with AION 2: could not {Action} the Startup shortcut", enabled ? "create" : "remove");
            }
        }

        /// <summary>The --watch mode: no window, starts the meter when the game starts. Returns false if a watcher already runs.</summary>
        public static bool RunWatcher(Application app)
        {
            var mutex = new Mutex(true, WatcherMutexName, out bool first);
            if (!first) { mutex.Dispose(); return false; }
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Already in a game session when the watcher starts (e.g. just enabled): don't open a second meter.
            bool launchedThisSession = IsGameRunning();
            var timer = new DispatcherTimer { Interval = WatchEvery };
            timer.Tick += (_, _) =>
            {
                try
                {
                    bool game = IsGameRunning();
                    if (!game) { launchedThisSession = false; return; }
                    if (launchedThisSession) return;
                    launchedThisSession = true;
                    if (MeterRunning()) return;
                    Log.Information("Start with AION 2: game started, starting the meter");
                    Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExePath) });
                }
                catch (Exception ex) { Log.Warning(ex, "Start with AION 2: watcher check failed"); }
            };
            timer.Start();
            GC.KeepAlive(mutex);
            app.Exit += (_, _) => mutex.Dispose();
            return true;
        }

        // ------------------------------------------------------------------ close with the game

        /// <summary>In the meter: close it once the game window has been gone for a while (only after it was seen).</summary>
        public static void StartCloseWithGame(IAppSettingsService settings, Window meter)
        {
            bool gameSeen = false;
            DateTime lastSeen = DateTime.Now;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += (_, _) =>
            {
                if (IsGameRunning()) { gameSeen = true; lastSeen = DateTime.Now; return; }
                if (!gameSeen || !settings.CloseWithGame || DateTime.Now - lastSeen < CloseAfterGameGone) return;
                timer.Stop();
                Log.Information("Close with AION 2: the game has closed, closing the meter");
                meter.Close(); // same path as closing the meter yourself: fights are saved to History
            };
            timer.Start();
        }
    }
}
