using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AionDpsMeter.UI.ViewModels;

namespace AionDpsMeter.UI.Views
{
    /// <summary>
    /// Fight result notification: after a boss fight a small card slides into the bottom-right corner of the
    /// game screen (above any boss alert toasts) with the boss, time, your rank and the top players. It hides
    /// after a few seconds (not while the mouse is on it); clicking it opens the full fight summary.
    /// Never takes focus from the game.
    /// </summary>
    public sealed class FightResultToast : Window
    {
        private static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(10);
        private const double Margin16 = 16;
        private readonly DispatcherTimer closeTimer = new() { Interval = ShownFor };
        private readonly Rect screen;

        public FightResultToast(FightSummary summary, Rect gameScreen, double overlayScale, Action openDetails)
        {
            screen = gameScreen;
            Title = "AION2 Fight Result";
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

            string accent = summary.Killed ? "#4ade80" : "#ff9a3c";
            var stack = new StackPanel { Width = 300 };

            // Header: result + boss + time, ✕ on the right.
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(Text(summary.Killed ? "BOSS KILLED" : "FIGHT ENDED", 9.5, accent, FontWeights.Bold));
            var boss = Text(summary.Boss, 14, "#ffffff", FontWeights.Bold);
            boss.TextTrimming = TextTrimming.CharacterEllipsis;
            titles.Children.Add(boss);
            head.Children.Add(titles);
            var time = Text($"{(int)summary.Duration.TotalMinutes}:{summary.Duration.Seconds:00}", 14, "#c8ccd8", FontWeights.SemiBold);
            time.Margin = new Thickness(8, 12, 8, 0);
            var close = Text("✕", 11, "#9aa0b0", FontWeights.Bold);
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Cursor = Cursors.Hand;
            close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(time);
            right.Children.Add(close);
            Grid.SetColumn(right, 1);
            head.Children.Add(right);
            stack.Children.Add(head);

            // You: rank, DPS, share.
            int rank = summary.Rows.ToList().FindIndex(r => r.IsUser);
            if (rank >= 0)
            {
                var me = summary.Rows[rank];
                // Alliance: others' hits never reach you, so no rank; the share is of the boss's HP.
                var line = Text(summary.Alliance
                    ? $"You: {DamageFormatter.Format(me.Dps)}/s · {me.Share:F1}% of the boss · alliance (others not visible)"
                    : $"You: #{rank + 1} of {summary.Rows.Count} · {DamageFormatter.Format(me.Dps)}/s · {me.Share:F0}%", 11.5, "#f2c14e", FontWeights.SemiBold);
                line.Margin = new Thickness(0, 4, 0, 2);
                stack.Children.Add(line);
            }
            // Progress on this boss level (vs your earlier fights at the same max HP), or the plain record note.
            if (summary.BestNote is not null)
            {
                var note = Text(summary.BestNote, 10.5, summary.NewBest ? "#f2c14e" : "#a9adbd", summary.NewBest ? FontWeights.Bold : FontWeights.Normal);
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(0, 0, 0, 2);
                stack.Children.Add(note);
            }
            else if (summary.NewBest) stack.Children.Add(Text("🏆 New personal best!", 11, "#f2c14e", FontWeights.Bold));

            // Players with share bars: the whole party (same cap as the summary card).
            double top = summary.Rows.Count > 0 ? Math.Max(1, summary.Rows.Max(r => r.Dps)) : 1;
            foreach (var row in summary.Rows.Take(8))
            {
                var grid = new Grid { Margin = new Thickness(0, 3, 0, 0), Height = 18 };
                var bar = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3),
                    Width = 300 * row.Dps / top,
                    Background = new SolidColorBrush(row.IsUser ? Color.FromArgb(110, 242, 193, 78) : Color.FromArgb(70, 143, 180, 255)),
                };
                grid.Children.Add(bar);
                var name = Text("  " + row.Name, 11, row.IsUser ? "#ffffff" : "#e8e8ee", row.IsUser ? FontWeights.Bold : FontWeights.SemiBold);
                name.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(name);
                var dps = Text($"{DamageFormatter.Format(row.Dps)}/s  {row.Share:F0}%  ", 11, "#ffffff", FontWeights.SemiBold);
                dps.HorizontalAlignment = HorizontalAlignment.Right;
                dps.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(dps);
                stack.Children.Add(grid);
            }
            var hint = Text(summary.Copied ? "📋 Result image copied: paste it with Ctrl+V · click for details" : "Click for details", 10,
                summary.Copied ? "#4ade80" : "#7d8190", summary.Copied ? FontWeights.SemiBold : FontWeights.Normal);
            hint.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(hint);

            var card = new Border
            {
                Margin = new Thickness(8), Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)),
                Background = new SolidColorBrush(Color.FromArgb(242, 13, 15, 21)), Child = stack, Cursor = Cursors.Hand,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.7, Color = (Color)ColorConverter.ConvertFromString(accent) },
                LayoutTransform = new ScaleTransform(Math.Clamp(overlayScale, 0.75, 3.0), Math.Clamp(overlayScale, 0.75, 3.0)),
            };
            card.MouseLeftButtonUp += (_, _) => { Close(); openDetails(); };
            Content = card;

            closeTimer.Tick += (_, _) => { if (!IsMouseOver) Close(); };
            Closed += (_, _) => closeTimer.Stop();
            SizeChanged += (_, _) => Place();
            Loaded += (_, _) =>
            {
                Place();
                var move = new TranslateTransform(340, 0);
                card.RenderTransform = move;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(340, 0, TimeSpan.FromMilliseconds(350)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                closeTimer.Start();
            };
        }

        /// <summary>Bottom-right of the game screen, above the boss alert toasts when they are showing.</summary>
        private void Place()
        {
            double above = Application.Current.Windows.OfType<BossAlertWindow>().FirstOrDefault(w => w.IsVisible)?.ActualHeight ?? 0;
            Left = screen.Right - ActualWidth - Margin16 + 8;
            Top = screen.Bottom - ActualHeight - Margin16 - above;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        private static TextBlock Text(string text, double size, string color, FontWeight weight) => new()
        {
            Text = text, FontSize = size, FontWeight = weight,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
        };

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
