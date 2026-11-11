using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.UI.Pages;
using AionDpsMeter.UI.Services.Windowing;
using AionDpsMeter.UI.Utils;
using AionDpsMeter.UI.ViewModels;

namespace AionDpsMeter.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly IAppSettingsService settingsService;
        private DispatcherTimer? _saveBoundsTimer;
        private GlobalHotkey? _globalHotkey;
        private GlobalHotkey? _timersHotkey;
        private GlobalHotkey? _bossRecallHotkey;
        private GlobalHotkey? _lastFightHotkey;
        private GlobalHotkey? _recordHotkey;
        private GlobalHotkey? _clearHotkey;
        private GlobalHotkey? _lockHotkey;
        private readonly IWindowManagerService windowManager;
        private readonly WindowHelper windowHelper;

        public MainWindow(MainViewModel viewModel, IAppSettingsService settingsService, IWindowManagerService windowManager, WindowHelper windowHelper)
        {
            InitializeComponent();
            DataContext = viewModel;
            this.settingsService      = settingsService;
            this.windowManager = windowManager;
            this.windowHelper = windowHelper;

            _saveBoundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) };
            _saveBoundsTimer.Tick += (_, _) => { _saveBoundsTimer.Stop(); SaveWindowBounds(); };

            RestoreWindowBounds();

            // Window Opacity is applied by the overlay to its backgrounds only.
            Topmost = settingsService.MeterOnTop;
            settingsService.SettingsChanged += (_, _) =>
                Dispatcher.InvokeAsync(() => { RegisterToggleHotkey(); Topmost = settingsService.MeterOnTop; });

            Loaded += (_, _) =>
            {
                ScreenHelper.EnsureOnScreen(this);
                RegisterToggleHotkey();
                // Created hidden at startup so it can pop up before a Spacetime Rift.
                _bossAlert ??= new BossAlertWindow(this); // 1 h / 15 min / 3 min boss spawn banner
                _readyAlert ??= new ReadyAlertWindow(this); // "READY" when a chosen skill comes off cooldown
                LayoutPanels(); // shows the ⏱ tab
                LocationChanged += (_, _) => LayoutPanels();
                SizeChanged += (_, _) => LayoutPanels();
                StateChanged += (_, _) => { RestoreHiddenByHotkey(); LayoutPanels(); };
                settingsService.SettingsChanged += (_, _) => Dispatcher.InvokeAsync(LayoutPanels);
            };
            windowManager.CloseAppCommand += OnCloseCommand;
        }

        private void OnCloseCommand(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => { CloseButton_Click(this, new RoutedEventArgs()); });
        }





        private void RegisterToggleHotkey()
        {
            _globalHotkey ??= new GlobalHotkey(this);
            _globalHotkey.HotkeyPressed -= ToggleWindowVisibility;
            _globalHotkey.Unregister();

            var (mods, vk) = HotkeyParser.Parse(settingsService.ToggleVisibilityHotkey);
            if (vk != 0)
            {
                _globalHotkey.Register(mods, vk);
                _globalHotkey.HotkeyPressed += ToggleWindowVisibility;
            }


            _clearHotkey ??= new GlobalHotkey(this);
            _clearHotkey.HotkeyPressed -= DpsOverlay.ClearFromHotkey;
            _clearHotkey.Unregister();
            var (clearMods, clearVk) = HotkeyParser.Parse(settingsService.ClearMeterHotkey);
            if (clearVk != 0)
            {
                _clearHotkey.Register(clearMods, clearVk, id: 9004);
                _clearHotkey.HotkeyPressed += DpsOverlay.ClearFromHotkey;
            }

            _lockHotkey ??= new GlobalHotkey(this);
            _lockHotkey.HotkeyPressed -= ToggleClickThroughLock;
            _lockHotkey.Unregister();
            var (lockMods, lockVk) = HotkeyParser.Parse(settingsService.LockHotkey);
            if (lockVk != 0)
            {
                _lockHotkey.Register(lockMods, lockVk, id: 9005);
                _lockHotkey.HotkeyPressed += ToggleClickThroughLock;
            }
            ApplyClickThrough();
            ApplyLayout();

            _timersHotkey ??= new GlobalHotkey(this);
            _timersHotkey.HotkeyPressed -= ToggleEventTimers;
            _timersHotkey.Unregister();
            var (timerMods, timerVk) = HotkeyParser.Parse(settingsService.EventTimersHotkey);
            if (timerVk != 0)
            {
                _timersHotkey.Register(timerMods, timerVk, id: 9003);
                _timersHotkey.HotkeyPressed += ToggleEventTimers;
            }

            _bossRecallHotkey ??= new GlobalHotkey(this);
            _bossRecallHotkey.HotkeyPressed -= ShowBossAlertsAgain;
            _bossRecallHotkey.Unregister();
            var (bossMods, bossVk) = HotkeyParser.Parse(settingsService.BossAlertsHotkey);
            if (bossVk != 0)
            {
                _bossRecallHotkey.Register(bossMods, bossVk, id: 9007);
                _bossRecallHotkey.HotkeyPressed += ShowBossAlertsAgain;
            }

            _lastFightHotkey ??= new GlobalHotkey(this);
            _lastFightHotkey.HotkeyPressed -= ToggleLastFight;
            _lastFightHotkey.Unregister();
            var (fightMods, fightVk) = HotkeyParser.Parse(settingsService.LastFightHotkey);
            if (fightVk != 0)
            {
                _lastFightHotkey.Register(fightMods, fightVk, id: 9009);
                _lastFightHotkey.HotkeyPressed += ToggleLastFight;
            }

            _recordHotkey ??= new GlobalHotkey(this);
            _recordHotkey.HotkeyPressed -= ToggleRecording;
            _recordHotkey.Unregister();
            var (recMods, recVk) = HotkeyParser.Parse(settingsService.RecordHotkey);
            if (recVk != 0)
            {
                _recordHotkey.Register(recMods, recVk, id: 9011);
                _recordHotkey.HotkeyPressed += ToggleRecording;
            }

        }

        private void ApplyLayout()
        {
            if (!IsVisible) Show(); // (the side layout is gone; an old setting of it shows the normal meter)
            LayoutPanels();
        }

        private void ToggleClickThroughLock() => settingsService.LockClickThrough = !settingsService.LockClickThrough;

        /// <summary>Click-through lock: the meter window ignores the mouse entirely (drawers/tab stay clickable).</summary>
        private System.Windows.Threading.DispatcherTimer? _ctrlWatch;

        private void ApplyClickThrough()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            // Holding Alt (the key that shows the cursor in AION 2) makes a locked meter clickable,
            // so Alt+click on the lock unlocks it.
            bool passThrough = settingsService.LockClickThrough && !AltHeld();
            // Ghost every meter window, not just this one: the drawers and ⏱ tab used to stay solid and
            // clickable, covering game menus that open under the meter.
            double opacity = settingsService.LockedOpacity;
            LockGhost.Apply(this, passThrough, opacity);

            // Watch the Alt key only while locked (cheap: one key-state read every 80 ms).
            if (settingsService.LockClickThrough && _ctrlWatch is null)
            {
                _ctrlWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                _ctrlWatch.Tick += (_, _) => ApplyClickThrough();
                _ctrlWatch.Start();
            }
            else if (!settingsService.LockClickThrough && _ctrlWatch is not null)
            {
                _ctrlWatch.Stop();
                _ctrlWatch = null;
            }
        }

        private static bool AltHeld() => LockGhost.AltHeld();

        private BossAlertWindow? _bossAlert;
        private ReadyAlertWindow? _readyAlert;

        /// <summary>Last fight result card (◀ ▶ inside for earlier ones).</summary>
        private void ToggleRecording() =>
            (App.AppHost.Services.GetService(typeof(AionDpsMeter.UI.Services.Recording.BossRecorder))
                as AionDpsMeter.UI.Services.Recording.BossRecorder)?.ToggleManual();

        private void ToggleLastFight() =>
            (App.AppHost.Services.GetService(typeof(AionDpsMeter.UI.Services.Records.FightSummaryService)) as AionDpsMeter.UI.Services.Records.FightSummaryService)?.ToggleLast();

        /// <summary>Boss pop-ups that hid after 10 s (or were closed) come back for another 10 s.</summary>
        private void ShowBossAlertsAgain() => _bossAlert?.ShowAgain();


        /// <summary>
        /// Events drawer slides out beside the meter (right side, or left when there's no room);
        /// the network panel sits on top of the meter (or under the visible meter if there's no room above).
        /// </summary>
        public void LayoutPanels()
        {

            // Nothing docks beside the meter any more (the events drawer became the Control Panel's Events tab).


        }

        /// <summary>Events hotkey / ⋯ menu: opens the Control Panel (characters, weekly progress, events).</summary>
        public void ToggleEventTimers() =>
            ((WindowHelper)App.AppHost.Services.GetService(typeof(WindowHelper))!).OpenControlPanel();

        /// <summary>Network analyzer: shows/hides the ping graph panel above the meter.</summary>
        private void ToggleWindowVisibility()
        {
            // Faded out by auto-hide: the hotkey brings it back instead of hiding it.
            if (WindowState != WindowState.Minimized && DpsOverlay.IsAutoHidden)
            {
                DpsOverlay.Wake();
                return;
            }
            // Minimized by the ━ button: the hotkey brings it back.
            if (WindowState == WindowState.Minimized && _hiddenByHotkey is null)
            {
                WindowState = WindowState.Normal;
                return;
            }

            // The meter minimizes to the taskbar (its button stays, click it to bring everything back); the
            // helper windows are hidden, not minimized: a minimized window without a taskbar button (dock,
            // party tabs, drawers) is parked by Windows as a strip at the bottom of the screen.
            if (_hiddenByHotkey is null)
            {
                _hiddenByHotkey = Application.Current.Windows.OfType<Window>().Where(w => w != this && w.IsVisible).ToList();
                foreach (var window in _hiddenByHotkey) window.Hide();
                WindowState = WindowState.Minimized;
            }
            else
            {
                WindowState = WindowState.Normal; // StateChanged brings the helper windows back
            }
        }

        /// <summary>Helper windows hidden by the show/hide hotkey (null = shown).</summary>
        private List<Window>? _hiddenByHotkey;

        /// <summary>Back from the hotkey's minimize (hotkey again, or a click on the taskbar button): restore the rest.</summary>
        private void RestoreHiddenByHotkey()
        {
            if (WindowState == WindowState.Minimized || _hiddenByHotkey is not { } toShow) return;
            _hiddenByHotkey = null;
            foreach (var window in toShow)
            {
                try { if (PresentationSource.FromVisual(window) is not null) window.Show(); }
                catch (InvalidOperationException) { /* closed meanwhile */ }
            }
            LayoutPanels();
        }


        private void RestoreWindowBounds()
        {
            var left   = settingsService.WindowLeft;
            var top    = settingsService.WindowTop;
            var width  = settingsService.WindowWidth;
            var height = settingsService.WindowHeight;

            if (!left.HasValue || !top.HasValue)
                return;

            double w = width.HasValue  ? Math.Max(MinWidth,  width.Value)  : Width;
            double h = height.HasValue ? Math.Max(MinHeight, height.Value) : Height;

            var wa = ScreenHelper.GetWorkingAreaForPoint(left.Value, top.Value);

            double l = Math.Max(wa.Left, Math.Min(left.Value, wa.Right  - w));
            double t = Math.Max(wa.Top,  Math.Min(top.Value,  wa.Bottom - h));

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left   = l;
            Top    = t;
            Width  = w;
            Height = h;
        }


        private void SaveWindowBounds()
        {
            if (WindowState != WindowState.Normal) return;
            settingsService.WindowLeft   = Left;
            settingsService.WindowTop    = Top;
            settingsService.WindowWidth  = Width;
            settingsService.WindowHeight = Height;
        }


        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _globalHotkey?.Dispose();
            _timersHotkey?.Dispose();
            _bossRecallHotkey?.Dispose();
            _lastFightHotkey?.Dispose();
            _recordHotkey?.Dispose();
            _clearHotkey?.Dispose();
            _lockHotkey?.Dispose();
            // Finish an in-progress boss recording so the file isn't lost.
            (App.AppHost.Services.GetService(typeof(AionDpsMeter.UI.Services.Recording.BossRecorder))
                as AionDpsMeter.UI.Services.Recording.BossRecorder)?.Shutdown();
            if (DataContext is MainViewModel viewModel)
                viewModel.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            _saveBoundsTimer?.Stop();
            _saveBoundsTimer = null;
            SaveWindowBounds();
            DpsOverlay.Dispose();
            if (DataContext is MainViewModel viewModel)
                viewModel.Dispose();
            base.OnClosed(e);
            // The meter window closing (Alt+F4, Windows) means the app is done; hidden helper
            // windows (settings, drawers) must not keep it running.
            Application.Current.Shutdown();
        }
    }
}