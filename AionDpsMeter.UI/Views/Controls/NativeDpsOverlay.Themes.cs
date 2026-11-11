using System.Windows.Media;
using AionDpsMeter.UI.ViewModels;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// Bar Colors setting: theme 0 keeps the original colouring (class colours or me/other);
    /// every other theme colours rows by their rank with its own palette, in every skin.
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        // Index = BarTheme setting value. Each entry is (from, to) per rank, cycled.
        private static readonly (Color From, Color To)[][] ThemePalettes =
        {
            Array.Empty<(Color, Color)>(), // 0: Default (class colours / me-other)
            new[] // 1: Ember
            {
                (Rgba(150, 20, 10, 0.55), Rgba(255, 94, 40, 0.92)),
                (Rgba(160, 50, 0, 0.55), Rgba(255, 150, 30, 0.9)),
                (Rgba(150, 90, 0, 0.55), Rgba(255, 200, 60, 0.88)),
                (Rgba(120, 30, 30, 0.55), Rgba(220, 80, 80, 0.86)),
            },
            new[] // 2: Ocean
            {
                (Rgba(10, 40, 120, 0.55), Rgba(40, 140, 255, 0.92)),
                (Rgba(0, 80, 110, 0.55), Rgba(30, 200, 230, 0.9)),
                (Rgba(20, 50, 90, 0.55), Rgba(80, 160, 220, 0.88)),
                (Rgba(0, 90, 90, 0.55), Rgba(40, 210, 190, 0.86)),
            },
            new[] // 3: Aurora
            {
                (Rgba(20, 110, 70, 0.55), Rgba(60, 230, 150, 0.92)),
                (Rgba(0, 90, 110, 0.55), Rgba(40, 200, 220, 0.9)),
                (Rgba(70, 40, 140, 0.55), Rgba(150, 100, 255, 0.9)),
                (Rgba(120, 30, 110, 0.55), Rgba(230, 90, 210, 0.88)),
            },
            new[] // 4: Rainbow
            {
                (Rgba(150, 20, 30, 0.55), Rgba(255, 70, 80, 0.92)),
                (Rgba(160, 80, 0, 0.55), Rgba(255, 170, 40, 0.9)),
                (Rgba(140, 130, 0, 0.55), Rgba(240, 225, 60, 0.9)),
                (Rgba(20, 120, 40, 0.55), Rgba(70, 220, 100, 0.9)),
                (Rgba(10, 70, 150, 0.55), Rgba(50, 150, 255, 0.9)),
                (Rgba(80, 30, 150, 0.55), Rgba(170, 100, 255, 0.9)),
            },
            new[] // 5: Royal Gold
            {
                (Rgba(120, 85, 10, 0.6), Rgba(242, 193, 78, 0.95)),
                (Rgba(100, 100, 110, 0.55), Rgba(205, 210, 220, 0.9)),
                (Rgba(110, 60, 25, 0.55), Rgba(205, 127, 50, 0.9)),
                (Rgba(60, 60, 70, 0.55), Rgba(130, 135, 150, 0.86)),
            },
            new[] // 6: Sakura
            {
                (Rgba(150, 30, 90, 0.55), Rgba(255, 120, 190, 0.92)),
                (Rgba(130, 50, 120, 0.55), Rgba(240, 150, 230, 0.9)),
                (Rgba(150, 70, 90, 0.55), Rgba(255, 170, 180, 0.88)),
                (Rgba(110, 60, 140, 0.55), Rgba(200, 160, 255, 0.86)),
            },
            new[] // 7: Toxic
            {
                (Rgba(60, 120, 0, 0.55), Rgba(170, 255, 40, 0.92)),
                (Rgba(0, 110, 60, 0.55), Rgba(40, 255, 150, 0.9)),
                (Rgba(90, 110, 0, 0.55), Rgba(220, 240, 60, 0.88)),
                (Rgba(30, 90, 50, 0.55), Rgba(100, 200, 120, 0.86)),
            },
            new[] // 8: Mono (your own row is gold)
            {
                (Rgba(70, 72, 80, 0.55), Rgba(170, 175, 190, 0.85)),
            },
        };

        private static readonly (Color, Color) MonoMeColors = (Rgba(120, 85, 10, 0.6), Rgba(242, 193, 78, 0.95));

        private int ActiveTheme => Math.Clamp(settingsService.BarTheme, 0, ThemePalettes.Length - 1);

        /// <summary>
        /// Settings → Class Colors: Always (UseClassColors), or In groups (ClassColorsInGroup) whenever more
        /// than one player is on the meter. Either overrides the Bar Colors theme so a party reads by class.
        /// </summary>
        private bool UseClassColorsNow(int theme) =>
            settingsService.UseClassColors ||
            (settingsService.ClassColorsInGroup && viewModel.Players.Count > 1);

        /// <summary>Cache key for the colours <see cref="GetBarColors"/> returns.</summary>
        private string ColorKey(PlayerRenderState player)
        {
            int theme = ActiveTheme;
            if (UseClassColorsNow(theme)) return $"c{player.ClassId}";
            if (theme == 0)
                return player.IsUser ? "me" : "other";
            if (theme == 8 && player.IsUser) return "t8me";
            int rank = Math.Max(0, viewModel.Players.IndexOf(player));
            return $"t{theme}r{rank % ThemePalettes[theme].Length}";
        }

        private (Color From, Color To) GetBarColors(PlayerRenderState player)
        {
            int theme = ActiveTheme;
            if (UseClassColorsNow(theme))
                return ClassColors.TryGetValue(player.ClassId, out var c) ? c : ClassColors["0"];
            if (theme == 0)
                return player.IsUser ? IsMeColors : OtherPlayerColors;
            if (theme == 8 && player.IsUser) return MonoMeColors;
            var palette = ThemePalettes[theme];
            int rank = Math.Max(0, viewModel.Players.IndexOf(player));
            return palette[rank % palette.Length];
        }
    }
}
