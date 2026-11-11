using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDpsMeter.UI.ViewModels;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// ⎘ button: copies the current fight as one short line for in-game chat, e.g.
    /// "Thamon 2:08 | 1.CardiB 10.0K/s 24.7% 2.Fubgun 9.9K/s 24.4% 3.Rushifa 7.5K/s 18.5%".
    /// Kept under the chat length limit by dropping lower ranks.
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        private const int ChatLimit = 200;

        private bool CopyFightForChat()
        {
            var players = viewModel.Players;
            if (players.Count == 0) return false;
            string target = string.IsNullOrWhiteSpace(viewModel.ActiveTargetName) ? "Fight" : viewModel.ActiveTargetName;
            string time = TimeSpan.TryParseExact(viewModel.CombatDuration, @"mm\:ss", CultureInfo.InvariantCulture, out var d)
                ? $"{(int)d.TotalMinutes}:{d.Seconds:00}"   // "02:08" -> "2:08", "00:26" -> "0:26"
                : viewModel.CombatDuration;
            var line = new StringBuilder($"{target} {time} |");
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                string name = p.PlayerNameDisplay.Split(" · ")[0]; // without the gear score
                string entry = $" {i + 1}.{name} {DamageFormatter.Format((long)p.Dps)}/s {p.DamagePercentage.ToString("F1", CultureInfo.InvariantCulture)}%";
                if (line.Length + entry.Length > ChatLimit) break;
                line.Append(entry);
            }
            SetClipboard(line.ToString());
            return true;
        }
    }
}
