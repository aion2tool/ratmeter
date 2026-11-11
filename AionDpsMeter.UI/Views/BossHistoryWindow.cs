using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDpsMeter.UI.Services.Records;
using AionDpsMeter.UI.ViewModels;

namespace AionDpsMeter.UI.Views
{
    /// <summary>
    /// Boss history card: your fights on one boss from History, grouped by boss level (max HP; the highest level
    /// first, since that's what you're working on), newest first: when, kill or not, fight time, your DPS with a
    /// bar against your best at that level, and the change from the fight before. ★ marks the best, the
    /// highlighted row is the fight the card was opened from. One character only (main and alts apart).
    /// </summary>
    public sealed class BossHistoryWindow : CardWindow
    {
        private const int RowsPerLevel = 10;

        public BossHistoryWindow(BossProgress progress, Guid currentSession, Window? near, double overlayScale, IReadOnlyList<WhyLine>? why = null)
            : base("AION2 Boss History", near, overlayScale, activatable: false)
        {
            var root = new StackPanel();
            int fights = progress.Levels.Sum(l => l.Fights.Count);
            root.Children.Add(Header("BOSS HISTORY", "#f2c14e", progress.Boss,
                $"{progress.Character ?? "You"} · {fights} fight{(fights == 1 ? "" : "s")} saved · DPS over the whole fight"));

            // What changed: this fight vs the 2 before it, biggest reasons first.
            if (why is { Count: > 0 })
            {
                root.Children.Add(SectionLabel("WHAT CHANGED · THIS FIGHT VS THE 2 BEFORE"));
                foreach (var line in why)
                {
                    var text = Text((line.Tone > 0 ? "▲ " : line.Tone < 0 ? "▼ " : "• ") + line.Text, 11,
                        line.Tone > 0 ? "#4ade80" : line.Tone < 0 ? "#ff8a7a" : "#c8ccd8", line == why[0] ? FontWeights.Bold : FontWeights.Normal);
                    text.TextWrapping = TextWrapping.Wrap;
                    text.TextTrimming = TextTrimming.None;
                    text.Margin = new Thickness(0, 1, 0, 2);
                    root.Children.Add(text);
                }
            }

            if (fights == 0)
                root.Children.Add(Text("No saved fights on this boss for this character yet.", 11.5, "#9aa0b0"));

            foreach (var level in progress.Levels.OrderByDescending(l => l.Number == 0 ? -1 : l.Number))
            {
                double best = level.BestDps;
                string title = level.Number > 0
                    ? $"LEVEL {level.Number} · {BossProgressService.Short(level.MaxHp)} HP · BEST {best:N0}/s"
                    : $"MAX HP UNKNOWN · BEST {best:N0}/s";
                root.Children.Add(SectionLabel(title));

                var shown = level.Fights.Take(RowsPerLevel).ToList();
                for (int i = 0; i < shown.Count; i++)
                {
                    var f = shown[i];
                    var previous = i + 1 < level.Fights.Count ? level.Fights[i + 1] : null; // fights are newest first
                    bool isBest = f.Dps >= best;
                    string change = previous is null ? "" : $"{(f.Dps >= previous.Dps ? "▲" : "▼")} {Math.Abs(f.Dps / previous.Dps - 1) * 100:F0}%";

                    var bar = new Border
                    {
                        Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left,
                        Width = 90 * Math.Clamp(f.Dps / Math.Max(1, best), 0, 1),
                        Background = Brush(isBest ? "#f2c14e" : "#8fb4ff"),
                    };
                    var dpsCell = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var dpsText = Text($"{(isBest ? "★ " : "")}{f.Dps:N0}/s", 11.5, isBest ? "#f2c14e" : "#ffffff", FontWeights.Bold);
                    dpsText.TextAlignment = TextAlignment.Right;
                    dpsCell.Children.Add(dpsText);
                    dpsCell.Children.Add(bar);

                    // History stores local time already.
                    var when = f.Start.ToString(f.Start.Date == DateTime.Today ? "'Today' HH:mm" : "MMM d HH:mm", CultureInfo.CurrentCulture);
                    root.Children.Add(TableRow([46, 104, 48], [
                        Text($"{(f.Killed ? "✓" : "✗")}  {when}", 11, f.Killed ? "#e8e8ee" : "#9aa0b0"),
                        Text($"{(int)f.Duration.TotalMinutes}:{f.Duration.Seconds:00}", 11, "#c8ccd8"),
                        dpsCell,
                        Text(change, 10.5, change.StartsWith('▲') ? "#4ade80" : "#ff8a7a", FontWeights.SemiBold),
                    ], f.SessionId == currentSession ? "#2a8fb4ff" : null));
                }
                if (level.Fights.Count > shown.Count)
                    root.Children.Add(Text($"… {level.Fights.Count - shown.Count} older fights", 10.5, "#6c7282"));
            }
            SetCard(root, 360);
        }
    }
}
