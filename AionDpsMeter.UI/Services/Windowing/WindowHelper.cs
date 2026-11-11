using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.Services.Services.Update;
using AionDpsMeter.Core.Windowing;
using AionDpsMeter.UI.Pages;
using AionDpsMeter.UI.ViewModels;
using AionDpsMeter.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AionDpsMeter.UI.Services.Windowing
{
    public class WindowHelper
    {
        private MainWindow MainWindow => serviceProvider.GetRequiredService<MainWindow>();

        private readonly IWindowManagerService windowManager;
        private readonly IServiceProvider serviceProvider;
        private readonly CombatSessionManager sessionManager;
        private readonly IAppSettingsService settingsService;
        private readonly UpdateCheckerService updateService;

        public WindowHelper(IWindowManagerService windowManager, IServiceProvider serviceProvider, CombatSessionManager sessionManager, IAppSettingsService settingsService, UpdateCheckerService updateService)
        {

            this.windowManager = windowManager;
            this.serviceProvider = serviceProvider;
            this.sessionManager = sessionManager;
            this.settingsService = settingsService;
            this.updateService = updateService;

        }

        public void OpenRequiredWindows() { } // (the buff / skill cooldown icon overlays are gone)


        public void OpenSettings()
        {
            // A normal app window: not on top of the game, in the taskbar, resizable, and it can live on another
            // screen (position and size are remembered).
            var win = new BlazorWindow(App.AppHost.Services, typeof(SettingsPage))
            {
                Title = "RATmeter Settings",
                Width = 980,
                Height = 700,
                MinWidth = 720,
                MinHeight = 480,
                Topmost = false,
                ShowInTaskbar = true,
                ResizeMode = System.Windows.ResizeMode.CanResize,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
            };
            System.Windows.Shell.WindowChrome.SetWindowChrome(win, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new System.Windows.Thickness(6),
                GlassFrameThickness = new System.Windows.Thickness(0),
                CornerRadius = new System.Windows.CornerRadius(12),
            });
            void Save()
            {
                if (win.WindowState == System.Windows.WindowState.Normal && win.IsLoaded)
                    settingsService.SetWindowBounds(WindowKey.Settings, new WindowBounds { Left = win.Left, Top = win.Top, Width = win.Width, Height = win.Height });
            }
            win.Deactivated += (_, _) => Save(); // after moving / resizing, when you go back to the game
            win.IsVisibleChanged += (_, _) => { if (!win.IsVisible) Save(); };
            windowManager.Open(WindowKey.Settings, win, isSingleton: true, owner: null, persistenceMode: WindowPersistenceMode.Bounds);
        }

        /// <summary>Control Panel (characters, weekly progress, events) — a normal app window like Settings.</summary>
        public void OpenControlPanel()
        {
            var win = new BlazorWindow(App.AppHost.Services, typeof(ControlPanelPage))
            {
                Title = "RATmeter Control Panel",
                Width = 1100,
                Height = 760,
                MinWidth = 820,
                MinHeight = 520,
                Topmost = false,
                ShowInTaskbar = true,
                ResizeMode = System.Windows.ResizeMode.CanResize,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
            };
            System.Windows.Shell.WindowChrome.SetWindowChrome(win, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new System.Windows.Thickness(6),
                GlassFrameThickness = new System.Windows.Thickness(0),
                CornerRadius = new System.Windows.CornerRadius(12),
            });
            windowManager.Open(WindowKey.ControlPanel, win, isSingleton: true, owner: null, persistenceMode: WindowPersistenceMode.Bounds);
        }

        /// <summary>Hide, not close — same WebView crash as Settings.</summary>
        public void CloseControlPanel() => windowManager.Hide(WindowKey.ControlPanel);

        public void CloseSettings()
        {
            // Hide, not close: closing disposed the page's WebView while its last
            // render was still queued, which crashed the app.
            windowManager.Hide(WindowKey.Settings);
        }
        public void OpenHistory()
        {
            var historyWindow = new HistoryWindow(sessionManager, settingsService)
            {
                DataContext = new ViewModels.History.HistoryViewModel(sessionManager, settingsService),
                Owner = MainWindow
            };
            windowManager.Open(WindowKey.History, historyWindow, true, null, MainWindow);
        }

        public void OpenWhatsNewWindow()
        {
            if (updateService.LatestReleaseInfo == null) return;
            var win = new WhatsNewWindow(updateService.LatestReleaseInfo, updateService)
            {
                Owner =  MainWindow
            };
            windowManager.Open(WindowKey.WhatsNew, win, true, null, MainWindow);
        }


        public void OpenPlayerDetails(PlayerRenderState player)
        {

            var detailsWindow = new PlayerDetailsWindow
            {
                DataContext = new PlayerDetailsViewModel(
                    sessionManager,
                    player.PlayerId,
                    player.PlayerNameDisplay,
                    player.ClassName,
                    null,
                    player.ClassIcon,
                    settingsService,
                    player.CombatPower,
                    player.ServerName),
                Owner = MainWindow
            };
            windowManager.Open(WindowKey.PlayerDetails, detailsWindow, false, null, MainWindow);
        }


    }
}
