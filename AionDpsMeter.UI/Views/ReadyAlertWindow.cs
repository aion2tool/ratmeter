using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace AionDpsMeter.UI.Views
{
    /// <summary>
    /// Skill ready alert for the skills chosen in Settings → Alerts → Skill Ready Alert (by name): a small
    /// round skill icon beside your character (right of centre, default camera). In the last 3 s of the
    /// cooldown it fades in dimmed with a gold ring sweeping round it and a countdown, so you can time the cast; the
    /// moment it is ready it pops with a burst ring, a short sound and a soft pulse ("READY"). Gone once you cast it
    /// or 8 s after your own last hit. Ready time comes from the game's own cooldown packets, so cooldown reductions
    /// are included. Display only: it never presses anything. Click-through, no focus.
    /// </summary>
    public sealed class ReadyAlertWindow : Window
    {
        /// <summary>Counts as "in combat" while you hit something this recently (others fighting nearby kept it up).</summary>
        private static readonly TimeSpan CombatWindow = TimeSpan.FromSeconds(8);
        /// <summary>Only skills you actually cast lately (so a skill not on your bar never alerts).</summary>
        private static readonly TimeSpan UsedWithin = TimeSpan.FromMinutes(10);
        /// <summary>
        /// A skill already ready when the fight starts waits this long before showing, so opening with it doesn't flash
        /// READY (your first hit can reach the meter before the game's cooldown update, and you may be charging it).
        /// </summary>
        private static readonly TimeSpan OpenerGrace = TimeSpan.FromSeconds(3);
        /// <summary>The ring starts filling this long before the skill is ready.</summary>
        private static readonly TimeSpan Lead = TimeSpan.FromSeconds(3);
        /// <summary>Icon centre as a fraction of the screen: beside your character (right of centre, at body height in the
        /// default camera), in side vision without covering the middle of the screen.</summary>
        private const double CenterX = 0.66, CenterY = 0.50;
        private const double Size = 72, IconSize = 46, Radius = 32;
        private static readonly string SoundFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify System Generic.wav");
        private static readonly Color Gold = Color.FromRgb(0xff, 0xc8, 0x3c);

        private readonly Window meter;
        private readonly IAppSettingsService settings;
        private readonly CooldownTracker cooldowns;
        private readonly CombatSessionManager sessions;
        private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(50) };

        private readonly Grid root = new() { Width = Size * 1.9 }; // room for the burst ring (grows to 1.7x)
        private readonly Image icon = new() { Width = IconSize, Height = IconSize, Stretch = Stretch.UniformToFill };
        private readonly Ellipse iconRim = new() { Width = IconSize + 4, Height = IconSize + 4, Stroke = new SolidColorBrush(Color.FromArgb(200, 20, 16, 8)), StrokeThickness = 2 };
        private readonly Ellipse track = new() { Width = Radius * 2, Height = Radius * 2, Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 200, 60)), StrokeThickness = 3 };
        private readonly System.Windows.Shapes.Path arc = new() { Stroke = new SolidColorBrush(Gold), StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        private readonly Ellipse burst = new() { Width = Size, Height = Size, Stroke = new SolidColorBrush(Gold), StrokeThickness = 3, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
        private readonly TextBlock count = new() { FontSize = 17, FontWeight = FontWeights.Black, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 1, Color = Colors.Black } };
        private readonly TextBlock label = new() { FontSize = 10.5, FontWeight = FontWeights.Black, FontFamily = new FontFamily("Segoe UI"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0), Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 0, Opacity = 1, Color = Colors.Black } };
        private readonly Grid dial = new() { Width = Size, Height = Size };
        private readonly DropShadowEffect glow = new() { BlurRadius = 18, ShadowDepth = 0, Color = Color.FromRgb(0xff, 0xb0, 0x20), Opacity = 0 };

        private readonly Dictionary<string, DateTime> alerted = new(StringComparer.OrdinalIgnoreCase); // skill → ReadyAt it popped for
        private enum State { Hidden, Soon, Ready }
        private State state = State.Hidden;
        private string? shownIcon;
        private DateTime combatSince = DateTime.MaxValue; // first of your hits in the current fight
        private Rect? screen;

        public ReadyAlertWindow(Window meter)
        {
            this.meter = meter;
            settings = App.AppHost.Services.GetRequiredService<IAppSettingsService>();
            cooldowns = App.AppHost.Services.GetRequiredService<CooldownTracker>();
            sessions = App.AppHost.Services.GetRequiredService<CombatSessionManager>();

            Title = "AION2 Skill Ready";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;

            icon.Clip = new EllipseGeometry(new Point(IconSize / 2, IconSize / 2), IconSize / 2, IconSize / 2);
            burst.RenderTransform = new ScaleTransform(1, 1);
            foreach (var e in new UIElement[] { burst, track, arc, iconRim, icon, count })
            {
                if (e is FrameworkElement fe && e != arc) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
                dial.Children.Add(e);
            }
            dial.Effect = glow;
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Size * 0.4, 0, Size * 0.2) };
            stack.Children.Add(dial);
            stack.Children.Add(label);
            root.Children.Add(stack);
            Content = root;

            tick.Tick += (_, _) => Tick();
            tick.Start();
            SizeChanged += (_, _) => Place();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        private void Tick()
        {
            var now = DateTime.Now;
            CooldownTracker.SkillCooldown? pick = null;
            TimeSpan left = TimeSpan.MaxValue;
            bool inCombat = now - sessions.LastUserDamageAt < CombatWindow;
            if (!inCombat) combatSince = DateTime.MaxValue;
            else if (combatSince == DateTime.MaxValue) combatSince = sessions.LastUserDamageAt;
            if (settings.ReadyAlert && inCombat)
            {
                // A ready skill wins; otherwise the one closest to ready within the last 3 s.
                foreach (var name in settings.ReadyAlertSkills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (cooldowns.Find(name) is not { } cd || now - cd.LastCast > UsedWithin) continue;
                    var l = cd.ReadyAt - now;
                    if (l > Lead || l >= left) continue;
                    if (cd.ReadyAt <= combatSince && now - combatSince < OpenerGrace) continue; // ready before the fight: give the opener a moment
                    pick = cd; left = l;
                    if (l <= TimeSpan.Zero) break;
                }
            }

            if (pick is not { } p)
            {
                if (state != State.Hidden)
                {
                    Serilog.Log.Information("[READY] hidden ({Why})", !settings.ReadyAlert ? "off" : now - sessions.LastUserDamageAt >= CombatWindow ? "out of combat" : "cast / not within 3 s");
                    state = State.Hidden;
                    Hide();
                }
                return;
            }

            if (p.Icon != shownIcon)
            {
                shownIcon = p.Icon;
                icon.Source = p.Icon is { Length: > 0 } url ? LoadIcon(url) : null;
            }
            if (state == State.Hidden) Appear();

            if (left > TimeSpan.Zero)
            {
                // Charging: dimmed icon, ring fills over the last 3 s, countdown.
                if (state != State.Soon)
                    Serilog.Log.Information("[READY] {Skill} soon: {Left:F1}s left (ready at {ReadyAt:HH:mm:ss.fff}, cast {Cast:HH:mm:ss.fff})", p.Name, left.TotalSeconds, p.ReadyAt, p.LastCast);
                state = State.Soon;
                double f = 1 - left.TotalSeconds / Lead.TotalSeconds;
                arc.Data = Arc(Math.Clamp(f, 0.001, 0.999));
                icon.Opacity = 0.45 + 0.35 * f;
                count.Text = left.TotalSeconds.ToString("0.0");
                label.Text = p.Name.ToUpperInvariant();
                label.Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 230, 170));
                return;
            }

            if (state != State.Ready || !alerted.TryGetValue(p.Name, out var poppedFor) || poppedFor != p.ReadyAt)
            {
                alerted[p.Name] = p.ReadyAt;
                Serilog.Log.Information("[READY] {Skill} READY (ready at {ReadyAt:HH:mm:ss.fff}, cast {Cast:HH:mm:ss.fff})", p.Name, p.ReadyAt, p.LastCast);
                state = State.Ready;
                Pop();
                if (settings.ReadyAlertSound) PlaySound();
            }
        }

        /// <summary>Fade + rise in at the spot above the character.</summary>
        private void Appear()
        {
            screen = ScreenUtil.GameWorkArea(meter.IsVisible ? meter : null); // the game's monitor, even if the meter is on another screen
            double scale = Math.Clamp(settings.OverlayScale, 0.75, 3.0);
            root.LayoutTransform = new ScaleTransform(scale, scale);
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            glow.Opacity = 0;
            arc.Visibility = Visibility.Visible;
            count.Visibility = Visibility.Visible;
            Show();
            Place();
            var rise = new TranslateTransform(0, 10);
            root.RenderTransform = rise;
            rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }

        /// <summary>Ready: full ring, bright icon, burst ring + bounce, then a soft endless pulse.</summary>
        private void Pop()
        {
            arc.Data = Arc(0.999);
            count.Visibility = Visibility.Collapsed;
            icon.Opacity = 1;
            label.Text = "READY";
            label.Foreground = new SolidColorBrush(Gold);

            var grow = new DoubleAnimation(0.7, 1.7, TimeSpan.FromMilliseconds(550)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            ((ScaleTransform)burst.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ((ScaleTransform)burst.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            burst.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(550)));

            var bounce = new ScaleTransform(1, 1, Size / 2, Size / 2);
            dial.RenderTransform = bounce;
            var pop = new DoubleAnimation(1.25, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut } };
            bounce.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            bounce.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

            glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        /// <summary>Ring from 12 o'clock, clockwise, for fraction 0..1.</summary>
        private static Geometry Arc(double f)
        {
            double c = Size / 2, a = f * 2 * Math.PI;
            var start = new Point(c, c - Radius);
            var end = new Point(c + Radius * Math.Sin(a), c - Radius * Math.Cos(a));
            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(Radius, Radius), 0, f > 0.5, SweepDirection.Clockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            return g;
        }

        private void Place()
        {
            var area = screen ?? SystemParameters.WorkArea;
            Left = area.Left + area.Width * CenterX - ActualWidth / 2;
            Top = area.Top + area.Height * CenterY - ActualHeight / 2;
        }

        private static BitmapImage? LoadIcon(string url)
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(url, UriKind.RelativeOrAbsolute);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                return img;
            }
            catch (Exception) { return null; }
        }

        private static void PlaySound()
        {
            try
            {
                if (File.Exists(SoundFile)) new SoundPlayer(SoundFile).Play(); // async
                else SystemSounds.Asterisk.Play();
            }
            catch (Exception) { /* no audio device */ }
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
