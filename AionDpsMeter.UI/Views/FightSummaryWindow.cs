using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AionDpsMeter.UI.ViewModels;

namespace AionDpsMeter.UI.Views
{
    /// <summary>Snapshot of a finished boss fight for the summary card.</summary>
    public sealed record FightSummary(
        string Boss, bool Killed, TimeSpan Duration, DateTime EndedAt,
        IReadOnlyList<FightSummaryRow> Rows, string? YourTopSkill, double YourTopSkillShare,
        string? BestNote, bool NewBest, Guid SessionId = default, string? LagNote = null, string? CooldownNote = null,
        string? Character = null, long BossMaxHp = 0, bool Alliance = false, bool Copied = false,
        IReadOnlyList<string>? Party = null,    // real names (Rows may be masked)
        AionDpsMeter.UI.Services.Records.KillPace? Pace = null);   // kill time vs your earlier kills (kills only)

    public sealed record FightSummaryRow(string Name, int ClassId, bool IsUser, long Total, double Dps, double Share, double CritRate, int Deaths, int CombatPower = 0, int GearScore = 0);

    /// <summary>
    /// Card shown after a boss fight: boss, kill/end, time, the ranking, your top skill and your
    /// personal-best comparison. Copy as chat text or as an image (paste into Discord).
    /// Closes itself after a while unless the mouse is on it. Never takes focus from the game.
    /// </summary>
    public sealed class FightSummaryWindow : Window
    {
        private static readonly TimeSpan AutoClose = TimeSpan.FromSeconds(45);
        private readonly FightSummary summary;
        private readonly Border card;
        private readonly DispatcherTimer closeTimer = new() { Interval = AutoClose };

        private readonly double scale;

        /// <summary>◀ / ▶ to earlier and later fights ("3 / 12"), from the fight summary service.</summary>
        public sealed record Browse(string Position, Action? Older, Action? Newer);
        private readonly Browse? browse;
        private StackPanel? buttonRow;
        private StackPanel? personalRow;
        private TextBlock? closeButton;

        /// <summary>The card as a picture for sharing (Discord, clipboard): no buttons and no personal lines.</summary>
        public static BitmapSource RenderImage(FightSummary summary, double overlayScale)
        {
            var w = new FightSummaryWindow(summary, null, overlayScale);
            w.closeTimer.Stop();
            var card = w.card;
            w.Content = null;
            if (w.buttonRow is not null) w.buttonRow.Visibility = Visibility.Collapsed;
            if (w.personalRow is not null) w.personalRow.Visibility = Visibility.Collapsed;
            if (w.closeButton is not null) w.closeButton.Visibility = Visibility.Collapsed;
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            card.Arrange(new Rect(card.DesiredSize));
            card.UpdateLayout();
            const double dpi = 144; // 1.5x: sharp in Discord
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth * dpi / 96 * (card.LayoutTransform is ScaleTransform st ? st.ScaleX : 1)),
                (int)Math.Ceiling(card.ActualHeight * dpi / 96 * (card.LayoutTransform is ScaleTransform st2 ? st2.ScaleY : 1)), dpi, dpi, PixelFormats.Pbgra32);
            bmp.Render(card);
            bmp.Freeze();
            w.Close();
            return bmp;
        }

        public FightSummaryWindow(FightSummary summary, Window? near, double overlayScale, Browse? browse = null)
        {
            scale = overlayScale;
            this.summary = summary;
            this.browse = browse;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "AION2 Fight Summary";
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;

            card = Build();
            card.LayoutTransform = new ScaleTransform(Math.Clamp(overlayScale, 0.75, 3.0), Math.Clamp(overlayScale, 0.75, 3.0));
            Content = card;

            // Beside the meter (right, or left if no room), always fully on the meter's monitor.
            // Placed once the card has its real size (ContentRendered), not on Loaded.
            Opacity = 0;
            ContentRendered += (_, _) =>
            {
                var area = ScreenUtil.WorkAreaOf(near ?? this);
                if (near is null)
                {
                    Left = area.Left + (area.Width - ActualWidth) / 2;
                    Top = area.Top + (area.Height - ActualHeight) / 3;
                }
                else
                {
                    bool right = near.Left + near.ActualWidth + 8 + ActualWidth <= area.Right;
                    Left = right ? near.Left + near.ActualWidth + 8 : near.Left - ActualWidth - 8;
                    Top = near.Top;
                }
                ScreenUtil.KeepInside(this, area);
                Opacity = 1;
            };
            closeTimer.Tick += (_, _) => { if (IsMouseOver) return; closeTimer.Stop(); Close(); };
            closeTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        private Border Build()
        {
            var root = new StackPanel { Width = 430 };

            // ---- header
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(Text("FIGHT SUMMARY", 9.5, "#8fb4ff", FontWeights.Bold));
            var boss = Text(summary.Boss, 16, "#ffffff", FontWeights.Bold);
            boss.TextTrimming = TextTrimming.CharacterEllipsis;
            titles.Children.Add(boss);
            long partyTotal = summary.Rows.Sum(r => r.Total);
            titles.Children.Add(Text($"{Clock(summary.Duration)} · party {Num(partyTotal)} dmg · {Num(partyTotal / Math.Max(1, summary.Duration.TotalSeconds))}/s", 11, "#9aa0b0"));
            header.Children.Add(titles);
            var badge = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Top,
                Background = Brush(summary.Killed ? "#305fe3a1" : "#30c8ccd8"), BorderBrush = Brush(summary.Killed ? "#C05fe3a1" : "#80c8ccd8"),
                BorderThickness = new Thickness(1),
                Child = Text(summary.Killed ? "KILL" : "END", 11, summary.Killed ? "#5fe3a1" : "#c8ccd8", FontWeights.Bold),
            };
            var close = Text("✕", 12, "#9aa0b0", FontWeights.Bold);
            close.Margin = new Thickness(10, 0, 0, 0);
            close.Cursor = Cursors.Hand;
            close.MouseLeftButtonUp += (_, _) => Close();
            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            badgeRow.Children.Add(badge);
            badgeRow.Children.Add(close);
            closeButton = close;
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0) };
            right.Children.Add(badgeRow);
            foreach (var line in PaceLines(summary))
                right.Children.Add(line);
            Grid.SetColumn(right, 1);
            header.Children.Add(right);
            root.Children.Add(header);

            // ---- ranking
            for (int i = 0; i < summary.Rows.Count && i < 8; i++)
                root.Children.Add(Row(i + 1, summary.Rows[i], summary.Rows[0].Total, estimate: summary.Alliance && !summary.Rows[i].IsUser));

            // ---- your fight (on your screen only, left out of shared images)
            if (summary.YourTopSkill is not null || summary.CooldownNote is not null || summary.BestNote is not null)
            {
                var you = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                if (summary.YourTopSkill is not null)
                    you.Children.Add(Text($"Your top skill: {summary.YourTopSkill} ({summary.YourTopSkillShare:F0}% of your damage)", 11, "#c8ccd8"));
                if (summary.CooldownNote is not null)
                    you.Children.Add(Text(summary.CooldownNote, 11, "#c8ccd8"));
                if (summary.BestNote is not null)
                    you.Children.Add(Text(summary.BestNote, 11.5, summary.NewBest ? "#f2c14e" : "#9aa0b0", summary.NewBest ? FontWeights.Bold : FontWeights.Normal));
                personalRow = you;
                root.Children.Add(you);
            }

            // ---- alliance fight: only your own hits reach you, so the ranking isn't real
            if (summary.Alliance)
                root.Children.Add(new TextBlock
                {
                    Text = "Alliance fight: the game only sends you your own hits, so only your line is exact. "
                         + "Others (marked ~) are estimates from DoT ticks, so their numbers and the order are partial. Shares are % of the boss's HP.",
                    FontSize = 11, Foreground = Brush("#8fb4ff"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
                });

            // ---- server stalls during the fight (lag that wasn't your fault)
            if (summary.LagNote is not null)
                root.Children.Add(new TextBlock
                {
                    Text = summary.LagNote, FontSize = 11, Foreground = Brush("#ff8a7a"), FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
                });

            // ---- buttons
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            buttonRow = buttons;
            if (browse is not null)
            {
                // Earlier / later boss fights of this session.
                if (browse.Older is { } older) buttons.Children.Add(Button("◀", older, copied: false));
                buttons.Children.Add(Text(browse.Position, 11, "#a9adbd", FontWeights.SemiBold));
                ((TextBlock)buttons.Children[^1]).Margin = new Thickness(6, 4, 0, 0);
                if (browse.Newer is { } newer) buttons.Children.Add(Button("▶", newer, copied: false));
            }
            if (summary.SessionId != Guid.Empty)
            {
                buttons.Children.Add(Button("Boss history & why", OpenBossHistory, copied: false));
                buttons.Children.Add(Button("Compare with best", CompareWithBest, copied: false));
            }
            buttons.Children.Add(Button("Copy text", CopyText));
            buttons.Children.Add(Button("Copy image", CopyImage));
            root.Children.Add(buttons);

            return new Border
            {
                Padding = new Thickness(14, 11, 14, 11),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(240, 13, 15, 21)),
                BorderBrush = Brush("#40ffffff"),
                BorderThickness = new Thickness(1),
                Child = root,
            };
        }

        /// <summary>
        /// Under the KILL badge: this kill's time vs your last kill at the same boss level ("▼ 0:12 faster"),
        /// then which kill this is and your best time. Nothing for wipes or when History has no level for it.
        /// </summary>
        private static IEnumerable<TextBlock> PaceLines(FightSummary s)
        {
            if (!s.Killed || s.Pace is not { } pace) yield break;
            TextBlock Line(string text, string color, FontWeight? weight = null)
            {
                var t = Text(text, 10.5, color, weight);
                t.HorizontalAlignment = HorizontalAlignment.Right;
                t.Margin = new Thickness(0, 1, 0, 0);
                return t;
            }
            if (pace.Last is not { } last)
            {
                yield return Line("First kill", "#5fe3a1", FontWeights.Bold);
                yield return Line(pace.Level > 0 ? $"at level {pace.Level}" : "on this boss", "#9aa0b0");
                yield break;
            }
            double diff = Math.Round((s.Duration - last).TotalSeconds);
            var gap = TimeSpan.FromSeconds(Math.Abs(diff));
            if (diff < 0) yield return Line($"▼ {Clock(gap)} faster", "#5fe3a1", FontWeights.Bold);
            else if (diff > 0) yield return Line($"▲ {Clock(gap)} slower", "#ff8a7a", FontWeights.Bold);
            else yield return Line("Same time", "#e8e8ee", FontWeights.Bold);
            yield return Line($"than last kill ({Clock(last)})", "#9aa0b0");
            bool newBest = pace.Best is { } best && s.Duration < best;
            yield return newBest
                ? Line($"Kill #{pace.KillNumber} · new best!", "#f2c14e", FontWeights.Bold)
                : Line($"Kill #{pace.KillNumber} · best {Clock(pace.Best ?? s.Duration)}", "#9aa0b0");
        }

        private static FrameworkElement Row(int rank, FightSummaryRow r, long top, bool estimate = false)
        {
            var grid = new Grid { Height = 26, Margin = new Thickness(0, 1, 0, 1) };
            var fill = new Border
            {
                CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brush("#26ffffff"), Opacity = estimate ? 0.6 : 1,
            };
            grid.Children.Add(fill);
            grid.SizeChanged += (_, _) => fill.Width = Math.Max(4, grid.ActualWidth * (top > 0 ? (double)r.Total / top : 0));

            var content = new Grid { Margin = new Thickness(6, 0, 8, 0) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // GS / CP (right-aligned)
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });   // DPS, fixed so rows line up
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });   // crit
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            var rankText = Text(rank.ToString(CultureInfo.CurrentCulture), 11, "#9aa0b0", FontWeights.Bold);
            content.Children.Add(rankText);
            var icon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Source = ClassIcon(r.ClassId) };
            Grid.SetColumn(icon, 1);
            content.Children.Add(icon);
            var name = Text(r.Name, 12, "#ffffff", FontWeights.SemiBold);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 2);
            content.Children.Add(name);
            if (r.CombatPower > 0 || r.GearScore > 0)
            {
                // Gear score (blue) / combat power (gold, same as the meter's pill); either may be unknown.
                var scores = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                if (r.GearScore > 0)
                    scores.Children.Add(Text($"GS {Num(r.GearScore)}", 10.5, "#8fb4ff", FontWeights.Bold));
                if (r.GearScore > 0 && r.CombatPower > 0)
                    scores.Children.Add(Text(" / ", 10.5, "#9aa0b0"));
                if (r.CombatPower > 0)
                    scores.Children.Add(Text($"CP {DamageFormatter.Format(r.CombatPower)}", 10.5, "#ffd76a", FontWeights.Bold));
                Grid.SetColumn(scores, 3);
                content.Children.Add(scores);
            }
            var dps = Text($"{(estimate ? "~" : "")}{Num(r.Dps)}/s", 12, estimate ? "#c8ccd8" : "#ffffff", FontWeights.Bold);   // "~" = estimate: alliance fights only show others through DoT ticks
            dps.Margin = new Thickness(0, 0, 4, 0);
            dps.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(dps, 4);
            content.Children.Add(dps);
            if (!estimate)   // alliance fights: others' crits aren't sent, so only real rows get one
            {
                var crit = Text($"crit {r.CritRate:F0}%", 10.5, "#a9adbd", FontWeights.SemiBold);
                crit.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(crit, 5);
                content.Children.Add(crit);
            }
            var share = Text($"{(estimate ? "~" : "")}{r.Share:F1}%", 11, estimate ? "#a89868" : "#f2c14e", FontWeights.Bold);
            share.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(share, 6);
            content.Children.Add(share);
            grid.Children.Add(content);
            grid.ToolTip = $"{r.Name}: {Num(r.Total)} damage, {Num(r.Dps)} DPS, crit {r.CritRate:F1}%" + (r.CombatPower > 0 ? $", combat power {Num(r.CombatPower)}" : "") + (r.GearScore > 0 ? $", gear score {Num(r.GearScore)}" : "");
            return grid;
        }

        // ------------------------------------------------------------------ copy

        private void CopyText()
        {
            var sb = new StringBuilder($"{summary.Boss} {(summary.Killed ? "kill" : "end")} {Clock(summary.Duration)} |");
            for (int i = 0; i < summary.Rows.Count; i++)
            {
                var r = summary.Rows[i];
                string est = summary.Alliance && !r.IsUser ? "~" : "";
                string entry = $" {i + 1}.{r.Name} {est}{DamageFormatter.Format((long)r.Dps)}/s {est}{r.Share.ToString("F1", CultureInfo.InvariantCulture)}%";
                if (sb.Length + entry.Length > 200) break;
                sb.Append(entry);
            }
            TrySetClipboard(() => Clipboard.SetText(sb.ToString()));
        }

        private void CopyImage()
        {
            var bmp = RenderImage(summary, scale);
            TrySetClipboard(() => Clipboard.SetImage(bmp));
        }

        private static void TrySetClipboard(Action set)
        {
            try { set(); }
            catch (COMException) { /* clipboard busy; click again */ }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Your earlier fights on this boss, by boss level (max HP), for this character.</summary>
        private void OpenBossHistory()
        {
            if (App.AppHost.Services.GetService(typeof(AionDpsMeter.UI.Services.Records.BossProgressService)) is not AionDpsMeter.UI.Services.Records.BossProgressService progress) return;
            new BossHistoryWindow(progress.Get(summary.Boss, summary.Character), summary.SessionId, this, scale,
                progress.Explain(summary.Boss, summary.Character, summary.SessionId)).Show();
        }

        private void CompareWithBest()
        {
            var service = App.AppHost.Services.GetService(typeof(AionDpsMeter.UI.Services.Records.FightCompareService))
                as AionDpsMeter.UI.Services.Records.FightCompareService;
            if (service?.CompareWithBest(summary.SessionId, this) is { } problem)
                MessageBox.Show(problem, "Fight compare", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static FrameworkElement Button(string label, Action onClick, bool copied = true)
        {
            var b = new Border
            {
                Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(5),
                Background = Brush("#22ffffff"), BorderBrush = Brush("#40ffffff"), BorderThickness = new Thickness(1),
                Child = Text(label, 11, "#e8e8ee", FontWeights.SemiBold), Cursor = Cursors.Hand,
            };
            var text = (TextBlock)b.Child;
            b.MouseLeftButtonUp += (_, _) =>
            {
                onClick();
                if (!copied) return;
                text.Text = "Copied ✓";
                var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                back.Tick += (_, _) => { back.Stop(); text.Text = label; };
                back.Start();
            };
            return b;
        }

        private static ImageSource? ClassIcon(int classId)
        {
            try
            {
                string? path = AionDpsMeter.Core.GameData.Services.GameDataProvider.Instance.Classes.GetById(classId)?.Icon;
                if (string.IsNullOrEmpty(path)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri($"pack://application:,,,/AionDpsMeter.UI;component{path}");
                bmp.DecodePixelWidth = 36;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception) { return null; }
        }

        private static string Num(double v) => Math.Round(v).ToString("N0", CultureInfo.CurrentCulture);
        private static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        private static TextBlock Text(string text, double size, string color, FontWeight? weight = null) => new()
        {
            Text = text, FontSize = size, Foreground = Brush(color), FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center,
        };

        private static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
