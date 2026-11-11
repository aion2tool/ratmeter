using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>Small building blocks shared by the remaining skins (Standard, Compact, Slim).</summary>
    public sealed partial class NativeDpsOverlay
    {
        private static readonly FontFamily ClassicFont = new("Segoe UI");
        private static readonly NumberFormatInfo CommaThousands = new() { NumberGroupSeparator = ",", NumberDecimalDigits = 0 };
        private const string PartyGold = "#f2c14e";
        private const string DpsRateColor = "#9ecbff";

        /// <summary>Drops the current fights from the meter (finished boss fights stay in History).</summary>
        private static void ClearMeter() => App.AppHost.Services.GetRequiredService<CombatSessionManager>().Reset();

        private static void SetClipboard(string text)
        {
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.COMException) { /* clipboard busy; user can click again */ }
        }

        private static TextBlock ClassicText(string text, double size, string color, FontWeight weight) => new()
        {
            Text = text,
            FontFamily = ClassicFont,
            FontSize = size,
            FontWeight = weight,
            Foreground = BrushFrom(color),
        };

        private static T Frozen<T>(T brush) where T : Freezable
        {
            brush.Freeze();
            return brush;
        }

        /// <summary>A player's bar: plain fill in the player's colour (the animated bar effects are gone).</summary>
        private void PaintBar(System.Windows.Controls.Border fill, PlayerRenderState player, Brush? plain = null) =>
            fill.Background = plain ?? GetFillBrush(player);

        private static string CommaNumber(double value) => Math.Round(value).ToString("#,0", CommaThousands);

        /// <summary>With Show Total Damage on, the skin still shows DPS beside the total.</summary>
        private static void SetRate(TextBlock rate, PlayerRenderState p, bool showTotal)
        {
            rate.Visibility = showTotal ? Visibility.Visible : Visibility.Collapsed;
            if (showTotal) rate.Text = CommaNumber(p.Dps) + "/s";
        }
    }
}
