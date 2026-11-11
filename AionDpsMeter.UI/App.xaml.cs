using AionDpsMeter.Services.Extensions;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.Services.Services.Timed;
using AionDpsMeter.Services.Services.Update;
using AionDpsMeter.Core.Windowing;
using AionDpsMeter.UI.Services.Windowing;
using AionDpsMeter.UI.ViewModels;
using AionDpsMeter.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Windows;

namespace AionDpsMeter.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IHost AppHost { get; private set; }

        public App()
        {



            AppHost = Host.CreateDefaultBuilder()
                .UseSerilog((context, services, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .ReadFrom.Configuration(context.Configuration);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddCombatHistoryPersistence("combat-history.db");

                    services.AddSingleton<IAppSettingsService, AppSettingsService>();
                    services.AddSingleton<UpdateCheckerService>();
                    services.AddSingleton<FilePacketWriter>();
                    services.AddSingleton<TcpStreamBuffer>();
                    services.AddSingleton<AionDpsMeter.Services.Services.Network.NetworkStats>();
                    services.AddSingleton<AionDpsMeter.Services.Services.Network.NetworkRecorder>();

                    //services.AddSingleton<IPacketCaptureDevice, FilePacketCaptureDevice>();
                    services.AddSingleton<IPacketCaptureDevice, CaptureDevice>();

                    services.AddSingleton<EntityTracker>();
                    services.AddKeyedSingleton<ITimedEventTracker, BuffTimedEventTracker>("Buffs");
                    services.AddKeyedSingleton<ITimedEventTracker, SkillCdTimedEventTracker>("SkillCd");
                    services.AddSingleton<CombatSessionManager>();
                    services.AddSingleton<ZoneChangeDetector>();
                    services.AddSingleton<CooldownTracker>();
                    services.AddPacketProcessingRouting();
                    services.AddSingleton<IPacketService, PacketPipelineService>();

                    services.AddWindowManager();


                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Records.DiscordPoster>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Records.FightSummaryService>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Records.PersonalBestService>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Records.BossProgressService>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Records.FightCompareService>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Timers.EventTimerService>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Timers.KillTimerService>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Timers.RegionAutoDetector>();
                    services.AddSingleton<AionDpsMeter.Services.Services.FieldBosses.FieldBossBoard>();
                    services.AddSingleton<AionDpsMeter.Services.Services.Roster.RosterEvents>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Roster.RosterService>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Timers.FieldBossMaps>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Pets.PetCatalogDownloader>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Pets.PetTracker>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Timers.FieldBossTimers>();
                    services.AddSingleton<AionDpsMeter.UI.Services.Recording.BossRecorder>();
                    services.AddSingleton<SettingsWindow>();
                    services.AddWpfBlazorWebView();

                })
                .Build();
        }

        // Held for the app's lifetime: a second copy would capture the same traffic twice
        // (or lock onto the wrong connection) and write duplicate logs.
        private static System.Threading.Mutex? singleInstance;

        private bool watchMode;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Started from the Windows Startup folder: only watch for the game and start the meter with it.
            if (e.Args.Contains(AionDpsMeter.UI.Services.GameLink.GameLink.WatchArgument))
            {
                watchMode = true;
                if (!AionDpsMeter.UI.Services.GameLink.GameLink.RunWatcher(this)) Shutdown();
                return;
            }
            singleInstance = new System.Threading.Mutex(true, @"Local\AION2DpsMeter.SingleInstance", out bool firstInstance);
            if (!firstInstance)
            {
                MessageBox.Show($"The AION 2 DPS meter is already running.\nPress {AppHost.Services.GetRequiredService<IAppSettingsService>().ToggleVisibilityHotkey} to show it.", "AION2 DPS Meter",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            await AppHost.StartAsync();

            _ = AppHost.Services.GetRequiredService<ICombatHistoryStore>();

            var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
            var windowManager = AppHost.Services.GetRequiredService<IWindowManagerService>();
            var windowHelper = AppHost.Services.GetRequiredService<WindowHelper>();
            windowManager.Open(WindowKey.Main, mainWindow, true);
            windowHelper.OpenRequiredWindows();
            AppHost.Services.GetRequiredService<AionDpsMeter.UI.Services.Recording.BossRecorder>().Start();
            _ = AppHost.Services.GetRequiredService<ZoneChangeDetector>(); // auto reset on zone change
            _ = AppHost.Services.GetRequiredService<AionDpsMeter.UI.Services.Roster.RosterService>(); // Control Panel tracking runs from start
            _ = AppHost.Services.GetRequiredService<AionDpsMeter.UI.Services.Pets.PetTracker>(); // pet souls counted from start
            _ = AppHost.Services.GetRequiredService<AionDpsMeter.UI.Services.Timers.RegionAutoDetector>(); // Timer Region from your server
            _ = AppHost.Services.GetRequiredService<AionDpsMeter.Services.Services.Network.NetworkRecorder>(); // network-*.csv for lag hunting
            _ = AppHost.Services.GetRequiredService<AionDpsMeter.UI.Services.Records.PersonalBestService>(); // records boss bests
            _ = AppHost.Services.GetRequiredService<AionDpsMeter.UI.Services.Records.FightSummaryService>(); // card after boss fights
            _ = AppHost.Services.GetRequiredService<AionDpsMeter.UI.Services.Timers.KillTimerService>(); // Gartua / Lagta respawn timers
            _ = AppHost.Services.GetRequiredService<AionDpsMeter.UI.Services.Timers.FieldBossTimers>(); // exact field boss times from the map
            AionDpsMeter.UI.Services.GameLink.GameLink.StartCloseWithGame(AppHost.Services.GetRequiredService<IAppSettingsService>(), mainWindow);
            AionDpsMeter.UI.Services.Windowing.KeepOnTop.Start(this); // the game buries topmost windows; re-raise every 2 s
            base.OnStartup(e);
        }

        // A Blazor window (settings / floating overlays) can try to send a last render
        // to its WebView after that WebView was disposed. That shouldn't take the whole
        // meter down, so only that specific failure is logged and swallowed.
        private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            for (Exception? ex = e.Exception; ex is not null; ex = ex.InnerException)
            {
                if (ex is InvalidOperationException && ex.Message.Contains("WebView2 control is disposed", StringComparison.Ordinal))
                {
                    Log.Warning(e.Exception, "Ignored render sent to a disposed WebView");
                    e.Handled = true;
                    return;
                }
            }
            Log.Fatal(e.Exception, "Unhandled UI exception");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (watchMode) { Log.CloseAndFlush(); base.OnExit(e); return; }
            try
            {
                // Every exit path (Alt+F4, Windows shutdown, tray) lands here, not only the close button:
                // finish an in-progress recording before Environment.Exit kills ffmpeg. No-op if idle.
                AppHost.Services.GetService<AionDpsMeter.UI.Services.Recording.BossRecorder>()?.Shutdown();
                // Save first: finished boss fights go to History.
                var sessionManager = AppHost.Services.GetRequiredService<CombatSessionManager>();
                var historyStore = AppHost.Services.GetRequiredService<ICombatHistoryStore>();
                sessionManager.CompleteAndPersistActiveSessions();
                historyStore.FlushPendingSaves();
                // Give background services a moment to stop, but never wait forever.
                AppHost.StopAsync().Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Shutdown cleanup failed");
            }
            finally
            {
                Log.CloseAndFlush();
                base.OnExit(e);
                // Something (capture/driver threads) could keep the process alive with no windows,
                // which then also blocked starting the meter again. End the process for sure.
                Environment.Exit(e.ApplicationExitCode);
            }
        }
    }

}
