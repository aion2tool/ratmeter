using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.UI.Services.Records;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// 🏆 personal-best chip and ✚ healing toggle in the meter header.
    /// During a boss fight the chip compares your live DPS with your best on that boss (▲ / ▼ %);
    /// for a minute after beating your best it turns gold: "NEW PB".
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        private static readonly TimeSpan NewRecordShownFor = TimeSpan.FromMinutes(1);

        private readonly List<(Border Chip, TextBlock Text, TextBlock Tip)> bestChips = new();
        private readonly List<(Border Button, TextBlock Glyph)> healToggles = new();
        private PersonalBestService? bests;
        private CombatSessionManager? bestSessions;
        private bool bestPulsing;

        private FrameworkElement BestChip()
        {
            var text = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 10.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            var tip = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
            var chip = new Border
            {
                Height = 18, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(5, 0, 6, 0),
                CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center, Child = text, Visibility = Visibility.Collapsed,
                ToolTip = new ToolTip { Content = tip },
            };
            ToolTipService.SetInitialShowDelay(chip, 0);
            bestChips.Add((chip, text, tip));
            return chip;
        }

        private void UpdateBestChips()
        {
            if (bestChips.Count == 0) return;
            bests ??= App.AppHost.Services.GetService(typeof(PersonalBestService)) as PersonalBestService;
            bestSessions ??= App.AppHost.Services.GetService(typeof(CombatSessionManager)) as CombatSessionManager;
            if (bests is null || bestSessions is null) return;

            string? label = null, tipText = null;
            string color = "#9aa0b0";
            bool gold = false;

            if (bests.LastNewRecord is { } rec && DateTime.Now - rec.At < NewRecordShownFor)
            {
                gold = true;
                color = "#f2c14e";
                label = "🏆 NEW PB";
                double gain = rec.PreviousBest > 0 ? (rec.Dps / rec.PreviousBest - 1) * 100 : 0;
                tipText = $"New personal best on {rec.Boss}!\n{Num(rec.Dps)} DPS (previous {Num(rec.PreviousBest)}, +{gain:F1}%)";
            }
            else if (bestSessions.GetActiveBossUserDps() is { } live)
            {
                var record = bests.Get(live.Boss);
                if (record is null || record.BestDps <= 0)
                {
                    label = "🏆 first";
                    tipText = $"First recorded fight on {live.Boss}: this one sets your personal best.";
                }
                else
                {
                    double diff = (live.UserDps / record.BestDps - 1) * 100;
                    label = diff >= 0 ? $"🏆 ▲{diff:F0}%" : $"🏆 ▼{-diff:F0}%";
                    color = diff >= 0 ? "#4adfc4" : "#ff8a7a";
                    tipText = $"{live.Boss}\nNow: {Num(live.UserDps)} DPS\nBest: {Num(record.BestDps)} DPS ({record.BestAt:d})\nAverage: {Num(record.AverageDps)} DPS over {record.Fights} fight{(record.Fights == 1 ? "" : "s")}";
                }
            }

            var c = BrushColor(color);
            foreach (var (chip, text, tip) in bestChips)
            {
                if (label is null) { chip.Visibility = Visibility.Collapsed; continue; }
                chip.Visibility = Visibility.Visible;
                text.Text = label;
                text.Foreground = BrushFrom(color);
                tip.Text = tipText;
                chip.Background = BrushFrom($"#30{c.R:X2}{c.G:X2}{c.B:X2}");
                chip.BorderBrush = BrushFrom($"#B0{c.R:X2}{c.G:X2}{c.B:X2}");
                if (gold != bestPulsing)
                    chip.BeginAnimation(OpacityProperty, gold
                        ? new DoubleAnimation(1.0, 0.5, TimeSpan.FromMilliseconds(600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                        : null);
            }
            bestPulsing = gold;
        }

        private static string Num(double v) => Math.Round(v).ToString("N0", CultureInfo.CurrentCulture);

        private void UpdateHealToggles()
        {
            bool on = settingsService.ShowHealing;
            foreach (var (button, glyph) in healToggles)
            {
                glyph.Foreground = BrushFrom(on ? "#5fe3a1" : "#8b8fa3");
                button.Background = BrushFrom(on ? "#405fe3a1" : "#22ffffff");
                button.BorderBrush = BrushFrom(on ? "#C05fe3a1" : "#00000000");
            }
        }
    }
}
