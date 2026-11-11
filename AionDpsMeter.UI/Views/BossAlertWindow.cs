using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.UI.Services.Timers;
using Microsoft.Extensions.DependencyInjection;

namespace AionDpsMeter.UI.Views
{
    /// <summary>
    /// Boss spawn banner above the meter for the farmed scheduled bosses (Watcher Kaira, Guardian Lord Nahma,
    /// siege bosses). Comes up 1 h before the spawn (gold), turns orange at 15 min and red + pulsing at 3 min,
    /// with a different sound at each step. The 1 h, 15 min and "SPAWNED" banners hide on their own after a few
    /// seconds; the 3 min one stays until the boss is out. ✕ hides a boss until its next step. Never takes focus from the game.
    /// It is also the one place event alerts show (Settings → Timers → Event Pop-ups): a banner N minutes before a
    /// chosen event starts (stays until it starts), then a short LIVE banner.
    /// </summary>
    public sealed class BossAlertWindow : Window
    {
        private static readonly TimeSpan SpawnedShownFor = TimeSpan.FromMinutes(2); // boss "SPAWNED" (auto-hides after AutoHideAfter)
        /// <summary>The 1 h, 15 min and "spawned" banners slide away after this; the 3 min one stays until the spawn.</summary>
        private static readonly TimeSpan AutoHideAfter = TimeSpan.FromSeconds(10);
        private static readonly string MediaDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

        /// <summary>Boss steps from earliest to last: lead time, label, colour, sound; the last one stays up.</summary>
        private static readonly Stage[] Stages =
        [
            new(TimeSpan.FromMinutes(60), "1 HOUR",  "#f2c14e", "Windows Notify Calendar.wav"),
            new(TimeSpan.FromMinutes(15), "15 MIN",  "#ff9a3c", "Windows Proximity Notification.wav"),
            new(TimeSpan.FromMinutes(3),  "3 MIN · GET THERE", "#ff4d5e", "Alarm02.wav", Stays: true),
        ];
        private static readonly Stage Spawned = new(TimeSpan.Zero, "SPAWNED", "#4ade80", "tada.wav", Live: true);
        /// <summary>Field bosses (exact times from the in-game map) get one step: 5 min before, with a map of the spot.</summary>
        private Stage FieldSoon => new(TimeSpan.FromMinutes(settings.FieldBossLeadMinutes), $"{settings.FieldBossLeadMinutes} MIN · GET THERE", "#ff9a3c", "Windows Proximity Notification.wav");
        /// <summary>Shown by the recall hotkey when no field boss is within 5 min: the next one coming up.</summary>
        private static readonly Stage FieldNext = new(TimeSpan.FromHours(24), "NEXT UP", "#8fb4ff", null);
        private static readonly TimeSpan EventLiveShownFor = TimeSpan.FromMinutes(1);
        private static readonly Dictionary<string, string> EventKindColors = new()
        {
            ["pvp"] = "#ff6f63", ["abyss"] = "#c27bff", ["event"] = "#4adfc4", ["boss"] = "#f2c14e", ["reset"] = "#8fb4ff",
        };

        /// <summary>Where each boss shows up and the nearest Asmodian Kibelisk (wikily.gg). All are Abyss bosses both factions share.</summary>
        private static readonly Dictionary<string, (string Where, string Kibelisk)> Locations = new()
        {
            ["watcher-kaira"] = ("Lower Abyss · random spot (6), e.g. Black Fragments · Abandoned Island", "Siel's Right Wing"),
            ["guardian-nahma"] = ("Reshanta's Root / Shade Island / Lonely Island", "Sulfur Tree Island (Shade Is.: Siel's Left Wing)"),
            ["kill-gartua"] = ("Altgard · Isle of Immortality", "Remote Island"),
            ["kill-lagta"] = ("Altgard · Lagta Fortress", "Kumrica's Cellar"),
            ["siege-bosses"] = ("Tamasa: Reshanta's Root · Argo: Siel's Heart · Kaira: Branding Island", "Sulfur Tree Is. (Argo: Siel's Right Wing)"),
        };

        private readonly Window meter;
        private readonly IAppSettingsService settings;
        private readonly EventTimerService timers;
        private readonly KillTimerService killTimers;
        private readonly FieldBossTimers fieldBosses;
        private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly StackPanel rows = new();
        private readonly Dictionary<string, Row> rowsById = new();
        private readonly Dictionary<string, DateTime> played = new(); // boss|spawn|stage → when it first showed
        private readonly Dictionary<string, string> dismissed = new(); // boss id → stage key it was closed at
        private DateTime recalledAt = DateTime.MinValue; // recall hotkey: everything shows again for AutoHideAfter

        public BossAlertWindow(Window meter)
        {
            this.meter = meter;
            settings = App.AppHost.Services.GetRequiredService<IAppSettingsService>();
            timers = App.AppHost.Services.GetRequiredService<EventTimerService>();
            killTimers = App.AppHost.Services.GetRequiredService<KillTimerService>();
            fieldBosses = App.AppHost.Services.GetRequiredService<FieldBossTimers>();

            Title = "AION2 Boss Alert";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;
            Content = rows;

            tick.Tick += (_, _) => Tick();
            tick.Start();
            SizeChanged += (_, _) => Follow();
            meter.LocationChanged += (_, _) => Follow();
            meter.SizeChanged += (_, _) => Follow();
            meter.IsVisibleChanged += (_, _) => Follow();
            meter.StateChanged += (_, _) => Follow();
            _ = timers.RefreshIfStaleAsync();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        /// <summary>
        /// Recall hotkey: shows the current alerts again for 10 s, including ones that hid or were closed. With no
        /// field boss within 5 min, the next wanted one is shown with its countdown.
        /// </summary>
        public void ShowAgain()
        {
            recalledAt = DateTime.UtcNow;
            dismissed.Clear();
            Tick();
        }

        private bool Recalled(DateTime now) => now - recalledAt < AutoHideAfter;

        private void Tick()
        {
            _ = timers.RefreshIfStaleAsync();
            var now = DateTime.UtcNow;
            var wanted = settings.BossAlerts
                ? settings.BossAlertIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet()
                : [];

            var active = new List<(EventStatus Event, Stage Stage, DateTime Spawn)>();
            // Kill timers (Gartua / Lagta) only exist after a kill was recorded, so they always alert.
            foreach (var e in timers.GetStatus(settings.TimerRegion, now).Concat(settings.BossAlerts ? killTimers.GetStatus(now) : []))
            {
                if (!wanted.Contains(e.Id) && !e.Id.StartsWith("kill-", StringComparison.Ordinal)) continue;
                if (e.LiveStart is { } live && now - live < SpawnedShownFor) active.Add((e, Spawned, live));
                else if (e.Next is { } next && StageFor(next - now) is { } stage) active.Add((e, stage, next));
            }
            if (settings.FieldBossMapAlerts)
            {
                // Gartua / Lagta already have kill timer alerts while those are on.
                var killed = settings.BossAlerts ? KillTimerService.Bosses.Select(b => $"field-{b.SpawnId}").ToHashSet() : [];
                // The picked bosses (Settings → Timers & Bosses), else the higher-level ones (better drops) or all.
                var wantedField = fieldBosses.GetAlerts(now).Where(e => !killed.Contains(e.Id) &&
                    int.TryParse(e.Id.AsSpan(6), out int spawnId) &&
                    fieldBosses.IsWanted(spawnId, e.Name, settings.FieldBossPicks, settings.FieldBossAlertsHigherOnly)).ToList();
                bool anyField = false;
                var soon = FieldSoon;
                foreach (var e in wantedField)
                {
                    if (e.LiveStart is { } live) { active.Add((e, Spawned, live)); anyField = true; }
                    else if (e.Next is { } next && next - now <= soon.Lead) { active.Add((e, soon, next)); anyField = true; }
                }
                if (!anyField && Recalled(now) && wantedField.Where(e => e.Next is not null).MinBy(e => e.Next) is { Next: { } soonest } upcoming)
                    active.Add((upcoming, FieldNext, soonest));
            }

            foreach (var (e, stage, spawn) in active)
            {
                string stageKey = $"{spawn:O}|{stage.Label}";
                if (!played.TryGetValue($"{e.Id}|{stageKey}", out var firstShown))
                {
                    played[$"{e.Id}|{stageKey}"] = firstShown = now;
                    if (stage.Sound is not null) PlaySound(stage.Sound);
                }
                if (dismissed.TryGetValue(e.Id, out var closedAt) && closedAt != stageKey) dismissed.Remove(e.Id);
                if (!stage.Stays && now - Latest(firstShown) > AutoHideAfter) dismissed[e.Id] = stageKey;
            }
            if (played.Count > 200) played.Clear();
            AddEventAlerts(active, wanted, now);

            // Bottom-right toasts stack upward: the soonest alert sits at the bottom, nearest the corner.
            var shown = active.Where(a => !dismissed.ContainsKey(a.Event.Id)).OrderBy(a => a.Spawn).Take(3).Reverse().ToList();
            foreach (var id in rowsById.Keys.Except(shown.Select(s => s.Event.Id)).ToList())
            {
                rows.Children.Remove(rowsById[id].Root);
                rowsById.Remove(id);
            }
            foreach (var (e, stage, spawn) in shown)
            {
                if (!rowsById.TryGetValue(e.Id, out var row))
                {
                    var id = e.Id;
                    row = new Row(() => { dismissed[id] = rowsById[id].StageKey; Tick(); });
                    rowsById[e.Id] = row;
                    rows.Children.Add(row.Root);
                    SlideIn(row.Root);
                }
                row.Update(e, stage, spawn, now, SpotOf(e.Id), killTimers.AsmodianSide);
            }
            // Keep the on-screen order (soonest at the bottom) as alerts come and go.
            for (int i = 0; i < shown.Count; i++)
            {
                var root = rowsById[shown[i].Event.Id].Root;
                if (rows.Children.IndexOf(root) != i) { rows.Children.Remove(root); rows.Children.Insert(i, root); }
            }

            double scale = Math.Clamp(settings.OverlayScale, 0.75, 3.0);
            if (rows.LayoutTransform is not ScaleTransform st || st.ScaleX != scale) rows.LayoutTransform = new ScaleTransform(scale, scale);

            if (shown.Count == 0) { if (IsVisible) Hide(); }
            else if (!IsVisible) { Show(); Follow(); }
            else Follow();
        }

        /// <summary>Events chosen under Event Pop-ups (bosses already on the boss list are skipped).</summary>
        private void AddEventAlerts(List<(EventStatus Event, Stage Stage, DateTime Spawn)> active, HashSet<string> bossIds, DateTime now)
        {
            if (!settings.EventNotifications) return;
            var ids = settings.EventNotifyIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            var lead = TimeSpan.FromMinutes(Math.Clamp(settings.EventNotifyLeadMinutes, 1, 30));
            string? sound = settings.EventNotifySound ? "Windows Notify Calendar.wav" : null;
            foreach (var e in timers.GetStatus(settings.TimerRegion, now))
            {
                if (!ids.Contains(e.Id) || bossIds.Contains(e.Id)) continue;
                string color = EventKindColors.TryGetValue(e.Kind, out var c) ? c : "#8fb4ff";
                Stage? stage = null; DateTime at = default;
                if (e.LiveStart is { } live && now - live < EventLiveShownFor) { stage = new(TimeSpan.Zero, "LIVE", color, null, Live: true); at = live; }
                else if (e.Next is { } next && next - now <= lead) { stage = new(lead, $"STARTS IN {(int)lead.TotalMinutes} MIN", color, sound, Stays: true); at = next; }
                if (stage is null) continue;

                string stageKey = $"{at:O}|{stage.Label}";
                if (!played.TryGetValue($"{e.Id}|{stageKey}", out var firstShown))
                {
                    played[$"{e.Id}|{stageKey}"] = firstShown = now;
                    if (stage.Sound is not null) PlaySound(stage.Sound);
                }
                if (dismissed.TryGetValue(e.Id, out var closedAt) && closedAt != stageKey) dismissed.Remove(e.Id);
                if (!stage.Stays && now - Latest(firstShown) > AutoHideAfter) dismissed[e.Id] = stageKey;
                active.Add((e, stage, at));
            }
        }

        private BossSpot? SpotOf(string id) =>
            id.StartsWith("field-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(6), out int spawnId) ? fieldBosses.SpotOf(spawnId) : null;

        /// <summary>When an alert last came on screen: first shown, or the recall hotkey.</summary>
        private DateTime Latest(DateTime firstShown) => firstShown > recalledAt ? firstShown : recalledAt;

        private static Stage? StageFor(TimeSpan left)
        {
            Stage? found = null;
            foreach (var s in Stages) if (left <= s.Lead) found = s; // latest step reached
            return found;
        }

        private Rect? gameScreen;

        /// <summary>
        /// Bottom-right corner of the screen the meter is on (the game screen), like a notification toast;
        /// the bottom stays anchored as alerts are added or removed. Remembers that screen while the meter is
        /// hidden or minimized.
        /// </summary>
        private void Follow()
        {
            gameScreen = ScreenUtil.GameWorkArea(meter.IsVisible && meter.WindowState != WindowState.Minimized ? meter : null);
            if (!IsVisible || ActualWidth == 0) return;
            var area = gameScreen ?? SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - ToastMargin;
            Top = area.Bottom - ActualHeight - ToastMargin;
        }

        private const double ToastMargin = 16;

        /// <summary>New toast slides in from the right edge and fades in.</summary>
        private static void SlideIn(FrameworkElement toast)
        {
            var move = new TranslateTransform(340, 0);
            toast.RenderTransform = move;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(340, 0, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease });
            toast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        }

        private static void PlaySound(string file)
        {
            try
            {
                var path = Path.Combine(MediaDir, file);
                if (File.Exists(path)) new SoundPlayer(path).Play(); // async
                else SystemSounds.Exclamation.Play();
            }
            catch (Exception) { /* no audio device */ }
        }

        private sealed record Stage(TimeSpan Lead, string Label, string Color, string? Sound, bool Stays = false, bool Live = false);

        /// <summary>One boss: colour bar, icon, name + step, big countdown, shrinking bar, ✕.</summary>
        private sealed class Row
        {
            public Border Root { get; }
            public string StageKey { get; private set; } = "";
            private readonly Border accent = new() { Width = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 9, 0) };
            private readonly TextBlock icon = new() { FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            private readonly TextBlock stageText = new() { FontSize = 9.5, FontWeight = FontWeights.Bold };
            private readonly TextBlock name = new() { FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 };
            private readonly TextBlock where = new() { FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0xc8, 0xcc, 0xd8)), TextWrapping = TextWrapping.Wrap, MaxWidth = 230 };
            private readonly TextBlock kibelisk = new() { FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0xa8, 0x9b, 0xff)), TextWrapping = TextWrapping.Wrap, MaxWidth = 230 };
            private readonly TextBlock countdown = new() { FontSize = 20, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0), FontFamily = new FontFamily("Segoe UI Semibold") };
            private readonly Border progress = new() { Height = 2, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            private readonly DropShadowEffect glow = new() { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.8 };
            private readonly Image map = new() { Width = MapWidth, Height = MapHeight, Stretch = Stretch.None };
            private readonly Grid mapPanel = new() { Width = MapWidth, Height = MapHeight, Margin = new Thickness(0, 7, 0, 0), Visibility = Visibility.Collapsed, ClipToBounds = true };
            private readonly TextBlock mapCaption = new() { FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Padding = new Thickness(6, 2, 6, 3), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromArgb(200, 13, 15, 21)) };
            private string? lastLabel;
            private BossSpot? mapSpot;
            private const int MapWidth = 300, MapHeight = 170;

            public Row(Action dismiss)
            {
                var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                titles.Children.Add(stageText);
                titles.Children.Add(name);
                titles.Children.Add(where);
                titles.Children.Add(kibelisk);
                titles.Children.Add(progress);

                var close = new TextBlock { Text = "✕", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x9a, 0xa0, 0xb0)), VerticalAlignment = VerticalAlignment.Top, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Hide until the next warning" };
                close.MouseLeftButtonUp += (_, e) => { e.Handled = true; dismiss(); };

                var grid = new Grid();
                foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
                Add(grid, accent, 0); Add(grid, icon, 1); Add(grid, titles, 2); Add(grid, countdown, 3); Add(grid, close, 4);

                // Map of the spawn spot: the crop is centred on it, so the pin sits in the middle.
                mapPanel.Clip = new RectangleGeometry(new Rect(0, 0, MapWidth, MapHeight), 6, 6);
                mapPanel.Children.Add(map);
                var ring = new System.Windows.Shapes.Ellipse { Width = 34, Height = 34, Stroke = Brushes.Red, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
                var pulse = new ScaleTransform();
                ring.RenderTransform = pulse;
                var grow = new DoubleAnimation(0.3, 1.3, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever };
                pulse.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                pulse.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever });
                mapPanel.Children.Add(ring);
                mapPanel.Children.Add(new System.Windows.Shapes.Ellipse { Width = 13, Height = 13, Fill = Brushes.Red, Stroke = Brushes.White, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                mapPanel.Children.Add(mapCaption);

                var body = new StackPanel();
                body.Children.Add(grid);
                body.Children.Add(mapPanel);
                Root = new Border
                {
                    MinWidth = 270, Margin = new Thickness(8, 4, 8, 4), Padding = new Thickness(8, 7, 9, 7),
                    CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5),
                    Background = new SolidColorBrush(Color.FromArgb(242, 13, 15, 21)),
                    Child = body, Effect = glow,
                };
            }

            private static void Add(Grid g, UIElement e, int col) { Grid.SetColumn(e, col); g.Children.Add(e); }

            private async void ShowMap(BossSpot? spot)
            {
                if (spot == mapSpot) return;
                mapSpot = spot;
                if (spot is null) { mapPanel.Visibility = Visibility.Collapsed; return; }
                mapCaption.Text = string.IsNullOrEmpty(spot.Level) ? spot.Area : $"{spot.Area} · {spot.Level}";
                var maps = App.AppHost.Services.GetRequiredService<FieldBossMaps>();
                var image = await Task.Run(() => maps.CropAsync(spot, MapWidth, MapHeight));
                if (spot != mapSpot) return; // changed while loading
                map.Source = image;
                mapPanel.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
            }

            public void Update(EventStatus e, Stage stage, DateTime spawn, DateTime now, BossSpot? spot, bool asmodian)
            {
                StageKey = $"{spawn:O}|{stage.Label}";
                var c = (Color)ColorConverter.ConvertFromString(stage.Color);
                bool spawned = stage.Live;
                bool isBoss = e.Kind == "boss" || e.Id.StartsWith("kill-", StringComparison.Ordinal);
                var left = spawn - now;

                icon.Text = string.IsNullOrEmpty(e.Icon) ? "⚔" : e.Icon;
                name.Text = e.Name;
                bool known = Locations.TryGetValue(e.Id, out var loc);
                where.Text = known ? "📍 " + loc.Where : spot is null ? "" : $"📍 {spot.Map} · {spot.Area}";
                kibelisk.Text = known ? "🌀 Asmo Kibelisk: " + loc.Kibelisk : "";
                where.Visibility = known || spot is not null ? Visibility.Visible : Visibility.Collapsed;
                kibelisk.Visibility = known && asmodian ? Visibility.Visible : Visibility.Collapsed;
                ShowMap(spot);
                stageText.Text = isBoss
                    ? (spawned ? "BOSS IS UP!" : $"BOSS SPAWN · {stage.Label}")
                    : (spawned ? "EVENT IS LIVE!" : $"EVENT · {stage.Label}");
                countdown.Text = spawned ? "NOW" : $"{(int)left.TotalMinutes}:{Math.Max(0, left.Seconds):00}";
                Root.ToolTip = $"{e.Name} spawns at {spawn.ToLocalTime():t}" + (string.IsNullOrEmpty(e.Description) ? "" : "\n\n" + e.Description);

                var brush = new SolidColorBrush(c);
                accent.Background = brush;
                stageText.Foreground = brush;
                countdown.Foreground = spawned ? brush : Brushes.White;
                progress.Background = brush;
                // Bar empties across the current step (60→15, 15→3, 3→0 min).
                double span = stage.Lead.TotalSeconds - (Stages.SkipWhile(s => s != stage).Skip(1).FirstOrDefault()?.Lead.TotalSeconds ?? 0);
                double floor = stage.Lead.TotalSeconds - span;
                progress.Width = spawned ? 230 : 230 * Math.Clamp((left.TotalSeconds - floor) / Math.Max(1, span), 0, 1);

                if (lastLabel == stage.Label) return;
                lastLabel = stage.Label;
                Root.BorderBrush = brush;
                Root.Background = new SolidColorBrush(Color.FromArgb(242, (byte)(13 + c.R / 12), (byte)(15 + c.G / 12), (byte)(21 + c.B / 12)));
                glow.Color = c;
                // Calm glow early on; the last 3 minutes and the spawn pulse to grab attention.
                bool urgent = stage.Stays || spawned;
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, urgent
                    ? new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(550)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                    : null);
                glow.Opacity = urgent ? 1.0 : 0.6;
                // Flash in on every new step.
                Root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(180)) { RepeatBehavior = new RepeatBehavior(3) });
            }
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
