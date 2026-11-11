using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AionDpsMeter.UI.Services.Timers;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// Event strip in the meter header (next to the mode button): one small icon per pinned event
    /// (default Spacetime Rift + Shugo Festival) with a short timer. Live event = the icon spins and glows
    /// and the timer shows how long is left; otherwise it is dimmed and shows time until it starts.
    /// Alerts for upcoming events and bosses are the banner above the meter (BossAlertWindow).
    /// Hover any icon for the details.
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        private static readonly TimeSpan AlertAfterStart = TimeSpan.FromMinutes(1);
        private static readonly Dictionary<string, string> EventKindColors = new()
        {
            ["pvp"] = "#ff6f63", ["abyss"] = "#c27bff", ["event"] = "#4adfc4",
            ["boss"] = "#f2c14e", ["reset"] = "#8fb4ff", ["milestone"] = "#ffffff",
        };

        private readonly List<StackPanel> eventStrips = new();

        private readonly List<Border> summaryButtons = new();
        private AionDpsMeter.UI.Services.Records.FightSummaryService? summaries;

        private void UpdateSummaryButtons()
        {
            summaries ??= App.AppHost.Services.GetService(typeof(AionDpsMeter.UI.Services.Records.FightSummaryService)) as AionDpsMeter.UI.Services.Records.FightSummaryService;
            var last = summaries?.Last;
            bool open = summaries?.IsOpen == true;
            foreach (var b in summaryButtons)
            {
                b.Visibility = last is null ? Visibility.Collapsed : Visibility.Visible;
                if (last is not null)
                    b.ToolTip = $"Last fight summary: {last.Boss} ({(last.Killed ? "kill" : "end")}, {(int)last.Duration.TotalMinutes}:{last.Duration.Seconds:00}) - click to {(open ? "close" : "open")}";
                b.Background = BrushFrom(open ? "#408fb4ff" : "#22ffffff");
            }
        }

        private readonly List<TextBlock> lockGlyphs = new();

        /// <summary>Lock icon: click-through on/off. Locked it glows gold; unlock with the hotkey or Alt+click.</summary>
        private FrameworkElement LockButton()
        {
            // Unlocked: click locks. Locked: the meter is only clickable while Alt is held, so a click here is an Alt+click: unlock.
            var button = GlowIcon(GlyphUnlock, "Lock", () => { settingsService.LockClickThrough = !settingsService.LockClickThrough; UpdateDrawerArrows(); }, out var glyph, Gold);
            button.Tag = ModeTag; // always visible, also in clean mode
            lockGlyphs.Add(glyph);
            return button;
        }

        private void UpdateDrawerArrows()
        {
            bool locked = settingsService.LockClickThrough;
            foreach (var g in lockGlyphs)
            {
                g.Text = locked ? GlyphLock : GlyphUnlock;
                SetIconActive(g, locked ? Gold : null);
                if (g.Parent is Border b) b.ToolTip = locked
                    ? $"Locked: clicks go through the meter. Alt+click here to unlock (or {settingsService.LockHotkey})"
                    : $"Lock: make the meter click-through ({settingsService.LockHotkey} toggles)";
            }
        }

        private DispatcherTimer? eventTick;
        private EventTimerService? eventTimers;

        private void EnsureEventTick()
        {
            if (eventTick is not null) return;
            eventTimers = App.AppHost.Services.GetService(typeof(EventTimerService)) as EventTimerService;
            eventTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            eventTick.Tick += (_, _) => UpdateEventBadges();
            eventTick.Start();
            EnsureMenuTick();
        }

        private void UpdateEventBadges()
        {
            if (disposed) { eventTick?.Stop(); return; }
            var now = DateTime.UtcNow;
            var shown = EventsToShow(now);
            UpdateDrawerArrows();
            UpdateAutoHide();
            UpdateBestChips();
            UpdateAlertChips();
            UpdateHealToggles();
            UpdateSummaryButtons();
            // Drop the "that's me" shine from rows that got hidden (fewer players now).
            foreach (var row in shimmers.Keys.Where(r => !r.IsVisible).ToList())
            {
                AdornerLayer.GetAdornerLayer(row)?.Remove(shimmers[row]);
                shimmers.Remove(row);
            }


            foreach (var strip in eventStrips)
            {
                while (strip.Children.Count < shown.Count) strip.Children.Add(new EventChip().Root);
                for (int i = 0; i < strip.Children.Count; i++)
                {
                    var chip = (EventChip)((FrameworkElement)strip.Children[i]).Tag;
                    if (i < shown.Count) chip.Update(shown[i].Event, shown[i].Alert, now);
                    else chip.Hide();
                }
            }
        }

        /// <summary>Pinned events (always) + chosen notify events while their alert window is on.</summary>
        private List<(EventStatus Event, bool Alert)> EventsToShow(DateTime now)
        {
            var result = new List<(EventStatus, bool)>();
            if (eventTimers is null) return result;
            var status = eventTimers.GetStatus(settingsService.TimerRegion, now);
            var pinned = SplitIds(settingsService.PinnedEventIds);
            var notify = settingsService.EventNotifications ? SplitIds(settingsService.EventNotifyIds) : [];
            var lead = TimeSpan.FromMinutes(Math.Clamp(settingsService.EventNotifyLeadMinutes, 1, 30));

            // Pinned events, in the order chosen; a pinned event that is due pulses. The alert itself
            // (banner + sound) is the boss/event pop-up above the meter, so nothing else chimes here.
            foreach (var id in pinned)
                if (status.FirstOrDefault(e => e.Id == id) is { } e) result.Add((e, IsAlerting(e, now, lead) && notify.Contains(id)));
            return result;
        }

        private readonly DateTime startedAt = DateTime.Now;
        private AionDpsMeter.Services.Services.Session.CombatSessionManager? combatSessions;
        private bool autoHidden;

        /// <summary>Out of combat for the set time: fade the meter out. Any hit (or hovering it) brings it back.</summary>
        private DateTime wokenAt = DateTime.MinValue;

        /// <summary>True while the meter is faded out by auto-hide.</summary>
        public bool IsAutoHidden => autoHidden;

        /// <summary>Show hotkey on a faded meter: bring it back for the auto-hide time (as if a fight just happened).</summary>
        public void Wake()
        {
            wokenAt = DateTime.Now;
            UpdateAutoHide();
        }

        private void UpdateAutoHide()
        {
            combatSessions ??= App.AppHost.Services.GetService(typeof(AionDpsMeter.Services.Services.Session.CombatSessionManager))
                as AionDpsMeter.Services.Services.Session.CombatSessionManager;
            var delay = TimeSpan.FromSeconds(Math.Clamp(settingsService.AutoHideSeconds, 5, 600));
            var lastActivity = combatSessions?.LastDamageAt ?? DateTime.MinValue;
            if (lastActivity < startedAt) lastActivity = startedAt; // stay visible for a while after starting
            if (lastActivity < wokenAt) lastActivity = wokenAt;     // woken by the show hotkey
            autoHidden = settingsService.AutoHideOutOfCombat && DateTime.Now - lastActivity > delay && !IsMouseOver
                && recorder?.IsRecording != true; // stays visible while recording, so the REC timer can be seen
            ApplyFade();
        }

        private bool faded;

        /// <summary>Fades the meter for auto-hide or an open game menu (see NativeDpsOverlay.MenuHide).</summary>
        private void ApplyFade()
        {
            bool hide = autoHidden || menuHidden;
            if (hide == faded) return;
            faded = hide;
            if (hide)
            {
                // The "that's me" outline lives on the adorner layer, which the fade doesn't cover.
                foreach (var (row, adorner) in shimmers.ToList())
                    AdornerLayer.GetAdornerLayer(row)?.Remove(adorner);
                shimmers.Clear();
            }
            skinHost.BeginAnimation(OpacityProperty, new DoubleAnimation(hide ? 0.0 : 1.0, TimeSpan.FromMilliseconds(hide ? 600 : 150)));
        }

        private static bool IsAlerting(EventStatus e, DateTime now, TimeSpan lead) =>
            (e.LiveStart is { } live && now - live < AlertAfterStart) || (e.Next is { } next && next - now <= lead);

        private static List<string> SplitIds(string ids) =>
            ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static string Short(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}"
            : t.TotalMinutes >= 1 ? $"{(int)Math.Ceiling(t.TotalMinutes - 0.001)}m"
            : $"{Math.Max(0, (int)t.TotalSeconds)}s";

        private static string Clock(DateTime utc)
        {
            var local = utc.ToLocalTime();
            string time = local.ToString("t", CultureInfo.CurrentCulture);
            return local.Date == DateTime.Today ? time : local.ToString("ddd ", CultureInfo.CurrentCulture) + time;
        }

        /// <summary>One icon + timer in the strip. Spins while its event is live, pulses while alerting.</summary>
        private sealed class EventChip
        {
            public Border Root { get; }
            private readonly TextBlock icon = new() { FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            private readonly TextBlock time = new() { FontFamily = new FontFamily("Segoe UI"), FontSize = 10.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
            private readonly TextBlock tip = new() { FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
            private readonly RotateTransform spin = new();
            private bool spinning, pulsing;

            public EventChip()
            {
                icon.RenderTransform = spin;
                icon.RenderTransformOrigin = new Point(0.5, 0.5);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(icon);
                row.Children.Add(time);
                Root = new Border
                {
                    Tag = this,
                    Height = 18,
                    Margin = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(4, 0, 5, 0),
                    CornerRadius = new CornerRadius(9),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = row,
                    ToolTip = new ToolTip { Content = tip },
                };
                ToolTipService.SetInitialShowDelay(Root, 0);
            }

            public void Update(EventStatus e, bool alert, DateTime now)
            {
                Root.Visibility = Visibility.Visible;
                var c = (Color)ColorConverter.ConvertFromString(EventKindColors.TryGetValue(e.Kind, out var hex) ? hex : "#8fb4ff");
                bool live = e.LiveStart is not null;
                icon.Text = e.Icon;

                string description = string.IsNullOrEmpty(e.Description) ? "" : "\n\n" + e.Description;
                if (live)
                {
                    var start = e.LiveStart!.Value;
                    bool portal = e.PortalOpen(now);
                    var end = portal ? start + e.Portal!.Value : start + e.Duration;
                    time.Text = Short(end - now);
                    tip.Text = portal
                        ? $"{e.Name}: portal OPEN, closes in {Short(end - now)} ({Clock(end)})\nEvent ends {Clock(start + e.Duration)}{description}"
                        : $"{e.Name}: LIVE, ends in {Short(end - now)} ({Clock(end)}){description}";
                }
                else if (e.Next is { } next)
                {
                    time.Text = Short(next - now);
                    tip.Text = $"{e.Name}: starts in {Short(next - now)} ({Clock(next)})" +
                               (e.Duration > TimeSpan.Zero ? $", lasts {(int)e.Duration.TotalMinutes} min" : "") + description;
                }

                // Live: full colour + spinning icon. Coming up soon (alert): pulsing. Otherwise dimmed.
                Root.Background = Brush(Color.FromArgb(live ? (byte)90 : (byte)40, c.R, c.G, c.B));
                Root.BorderBrush = Brush(Color.FromArgb(live || alert ? (byte)220 : (byte)70, c.R, c.G, c.B));
                time.Foreground = Brush(live ? Colors.White : Color.FromArgb(alert ? (byte)255 : (byte)170, c.R, c.G, c.B));
                icon.Opacity = live || alert ? 1.0 : 0.6;
                if (!live) Root.Effect = null;
                else if (Root.Effect is not System.Windows.Media.Effects.DropShadowEffect glow || glow.Color != c)
                    Root.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = c, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };

                if (live != spinning)
                {
                    spinning = live;
                    spin.BeginAnimation(RotateTransform.AngleProperty, live
                        ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever }
                        : null);
                    if (!live) spin.Angle = 0;
                }
                bool wantPulse = alert || live;
                if (wantPulse != pulsing)
                {
                    pulsing = wantPulse;
                    Root.BeginAnimation(UIElement.OpacityProperty, wantPulse
                        ? new DoubleAnimation(1.0, 0.55, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                        : null);
                    if (!wantPulse) Root.Opacity = 1.0;
                }
            }

            public void Hide()
            {
                if (Root.Visibility == Visibility.Collapsed) return;
                Root.Visibility = Visibility.Collapsed;
                spin.BeginAnimation(RotateTransform.AngleProperty, null);
                Root.BeginAnimation(UIElement.OpacityProperty, null);
                spinning = pulsing = false;
            }

            private static SolidColorBrush Brush(Color color)
            {
                var b = new SolidColorBrush(color);
                b.Freeze();
                return b;
            }
        }
    }
}
