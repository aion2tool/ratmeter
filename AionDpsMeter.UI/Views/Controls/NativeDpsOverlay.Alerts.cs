using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDpsMeter.Services.Services.Network;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// Header chip next to the 🏆 chip: "⚠ LAG 4.1s" for a short while after the game server went silent
    /// (a stall that isn't your PC).
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        private static readonly TimeSpan LagChipShownFor = TimeSpan.FromSeconds(20);

        private readonly List<(Border Chip, TextBlock Text)> lagChips = new();
        private NetworkStats? alertNetwork;

        private FrameworkElement LagChip()
        {
            var text = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 10.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Foreground = BrushFrom("#ff8a7a") };
            var chip = new Border
            {
                Height = 18, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(5, 0, 6, 0),
                CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
                Background = BrushFrom("#30ff5a5a"), BorderBrush = BrushFrom("#B0ff5a5a"),
                VerticalAlignment = VerticalAlignment.Center, Child = text, Visibility = Visibility.Collapsed,
            };
            ToolTipService.SetInitialShowDelay(chip, 0);
            lagChips.Add((chip, text));
            return chip;
        }

        /// <summary>Every second (with the other header badges).</summary>
        private void UpdateAlertChips()
        {
            if (lagChips.Count == 0) return;
            alertNetwork ??= App.AppHost.Services.GetService(typeof(NetworkStats)) as NetworkStats;
            var now = DateTime.Now;

            var stall = settingsService.LagStallAlert ? alertNetwork?.LastStall : null;
            bool showLag = stall is not null && now - (stall.Start + stall.Length) < LagChipShownFor;
            if (showLag)
            {
                int recent = alertNetwork!.GetStalls(now.AddMinutes(-30), now).Count;
                string tip = $"The game server sent nothing for {stall!.Length.TotalSeconds:F1} s at {stall.Start:HH:mm:ss} while your client kept sending.\n" +
                             $"That's server / route lag, not your PC. {recent} stall{(recent == 1 ? "" : "s")} in the last 30 min (see Logs\\log*.txt, [LAG]).";
                foreach (var (chip, text) in lagChips)
                {
                    text.Text = $"⚠ LAG {stall.Length.TotalSeconds:F1}s";
                    chip.ToolTip = tip;
                }
            }
            foreach (var (chip, _) in lagChips) chip.Visibility = showLag ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
