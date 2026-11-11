using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Documents;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// Ping for the text-only skin headers: "text  ·  75 ms", the ping part coloured by quality
    /// (same colours as the Standard skin's signal bars). Measured from the game's own server-time
    /// packets, so it follows whatever server/route you're on (ExitLag included) with no setup.
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        private readonly ConditionalWeakTable<TextBlock, string> pingTextCache = new();

        private void SetTextWithPing(TextBlock block, string text, string separator = "  ·  ")
        {
            string key = text + "\u0001" + viewModel.PingDisplay + "\u0001" + viewModel.PingColor;
            if (pingTextCache.TryGetValue(block, out var last) && last == key) return;
            pingTextCache.AddOrUpdate(block, key);

            block.Inlines.Clear();
            if (text.Length > 0)
            {
                block.Inlines.Add(new Run(text));
                block.Inlines.Add(new Run(separator));
            }
            block.Inlines.Add(new Run(viewModel.PingDisplay) { Foreground = BrushFrom(viewModel.PingColor) });
        }
    }
}
