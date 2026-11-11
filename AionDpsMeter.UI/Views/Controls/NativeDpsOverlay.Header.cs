using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// The header's controls, kept to two: the lock and a ⋯ menu that holds everything else (mode, party only,
    /// healing view, clear, copy, last fight, history, events, settings, hide, close). All icons come
    /// from one line-icon font (Segoe Fluent Icons, MDL2 on Windows 10) and glow softly on hover.
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        private static readonly FontFamily IconGlyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");
        private const string IconIdle = "#9aa0b4";
        private const string Accent = "#8fb4ff";
        private const string Gold = "#f2c14e";

        // Segoe Fluent / MDL2 glyphs
        private const string GlyphMore = "", GlyphLock = "", GlyphUnlock = "", GlyphClear = "",
            GlyphCopy = "", GlyphResult = "", GlyphHistory = "",
            GlyphEvents = "", GlyphSettings = "", GlyphHide = "", GlyphClose = "",
            GlyphMode = "", GlyphParty = "", GlyphHeal = "", GlyphCheck = "";

        /// <summary>Icon button: line icon, soft glow and highlight on hover. Returns the border; <paramref name="icon"/> for restyling.</summary>
        private static Border GlowIcon(string glyph, string tooltip, Action onClick, out TextBlock icon, string glow = Accent)
        {
            var effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0, Color = (Color)ColorConverter.ConvertFromString(glow) };
            icon = new TextBlock
            {
                Text = glyph, FontFamily = IconGlyphs, FontSize = 12, Foreground = BrushFrom(IconIdle),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = effect,
            };
            var text = icon;
            var button = new Border
            {
                Width = 22, Height = 20, Margin = new Thickness(2, 0, 0, 0), CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent, Child = icon, ToolTip = tooltip, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            };
            var hover = Frozen(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)));
            button.MouseEnter += (_, _) =>
            {
                button.Background = hover;
                if (text.Tag is not string) text.Foreground = Brushes.White; // an "active" icon keeps its colour
                effect.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.95, TimeSpan.FromMilliseconds(140)));
            };
            button.MouseLeave += (_, _) =>
            {
                button.Background = Brushes.Transparent;
                if (text.Tag is not string) text.Foreground = BrushFrom(IconIdle);
                effect.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(text.Tag is string ? 0.8 : 0, TimeSpan.FromMilliseconds(220)));
            };
            button.MouseLeftButtonDown += (_, e) => e.Handled = true; // don't start a drag
            button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
            return button;
        }

        /// <summary>Marks an icon as active (e.g. locked): its colour and a steady glow, or back to idle.</summary>
        private static void SetIconActive(TextBlock icon, string? color)
        {
            icon.Tag = color;
            icon.Foreground = BrushFrom(color ?? IconIdle);
            if (icon.Effect is DropShadowEffect e)
            {
                if (color is not null) e.Color = (Color)ColorConverter.ConvertFromString(color);
                e.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                e.Opacity = color is null ? 0 : 0.8;
            }
        }

        // ------------------------------------------------------------------ ⋯ menu

        private Popup? menu;

        /// <summary>⋯ button (each skin has its own; the menu opens under the one you clicked).</summary>
        private FrameworkElement MoreButton()
        {
            Border? button = null;
            button = GlowIcon(GlyphMore, "Menu", () => ToggleMenu(button!), out _);
            button.Tag = ModeTag; // always visible, also in clean mode
            return button;
        }

        private void ToggleMenu(FrameworkElement anchor)
        {
            if (menu is { IsOpen: true }) { menu.IsOpen = false; return; }
            menu = BuildMenu(anchor);
            menu.IsOpen = true;
        }

        private Popup BuildMenu(FrameworkElement anchor)
        {
            var main = Window.GetWindow(this) as MainWindow;
            var list = new StackPanel { MinWidth = 230 };
            string[] modeNames = ["Mob", "All", "Boss"];

            void Close() { if (menu is not null) menu.IsOpen = false; }
            void Add(string glyph, string label, string? hint, Action act, bool keepOpen = false, string? check = null)
            {
                list.Children.Add(MenuRow(glyph, label, hint, () => { if (!keepOpen) Close(); act(); }, check));
            }
            void Separator() => list.Children.Add(new Border { Height = 1, Margin = new Thickness(8, 5, 8, 5), Background = BrushFrom("#22ffffff") });

            Add(GlyphMode, "Meter mode", modeNames[CurrentMode], () =>
            {
                int next = (CurrentMode + 1) % 3;
                settingsService.AllTargetsMode = next == 1;
                settingsService.BossFocus = next == 2;
                ToggleMenu(anchor); ToggleMenu(anchor); // rebuild with the new mode
            }, keepOpen: true);
            Add(GlyphParty, "Only me + party", null, () => { settingsService.PartyOnly = !settingsService.PartyOnly; ToggleMenu(anchor); ToggleMenu(anchor); },
                keepOpen: true, check: settingsService.PartyOnly ? GlyphCheck : null);
            Add(GlyphHeal, "Healing view", null, () => { settingsService.ShowHealing = !settingsService.ShowHealing; UpdateHealToggles(); ToggleMenu(anchor); ToggleMenu(anchor); },
                keepOpen: true, check: settingsService.ShowHealing ? GlyphCheck : null);
            Separator();
            Add(GlyphClear, "Clear meter", settingsService.ClearMeterHotkey, ClearMeter);
            Add(GlyphCopy, "Copy fight for chat", null, () => CopyFightForChat());
            Add(GlyphResult, "Last fight result", settingsService.LastFightHotkey, () =>
                (App.AppHost.Services.GetService(typeof(AionDpsMeter.UI.Services.Records.FightSummaryService)) as AionDpsMeter.UI.Services.Records.FightSummaryService)?.ToggleLast());
            Add(GlyphHistory, "History", null, viewModel.OpenHistory);
            Separator();
            var recorder = App.AppHost.Services.GetService(typeof(AionDpsMeter.UI.Services.Recording.BossRecorder)) as AionDpsMeter.UI.Services.Recording.BossRecorder;
            bool recording = recorder?.IsRecording == true;
            Add(recording ? "" : "", recording ? "Stop recording" : "Start recording", settingsService.RecordHotkey,
                () => { recorder?.ToggleManual(); ToggleMenu(anchor); ToggleMenu(anchor); }, keepOpen: true);
            Separator();
            Add(GlyphEvents, "Control Panel", settingsService.EventTimersHotkey, () => main?.ToggleEventTimers());
            Separator();
            Add(GlyphSettings, "Settings", null, viewModel.OpenSettings);
            Add(GlyphHide, "Hide", settingsService.ToggleVisibilityHotkey, viewModel.Minimize);
            Add(GlyphClose, "Close meter", null, viewModel.Close);

            double scale = Math.Clamp(settingsService.OverlayScale, 0.75, 3.0);
            var card = new Border
            {
                Padding = new Thickness(5), CornerRadius = new CornerRadius(10), Margin = new Thickness(10),
                Background = Frozen(new SolidColorBrush(Color.FromArgb(244, 16, 18, 24))),
                BorderBrush = BrushFrom("#2effffff"), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black },
                Child = list, LayoutTransform = new ScaleTransform(scale, scale),
            };
            // Beside the meter, never over it: KeepOnTop re-raises the meter window and would paint over the menu.
            // WPF flips it to the left side when there's no room on the right.
            var meter = Window.GetWindow(this)?.Content as FrameworkElement ?? this;
            return new Popup
            {
                Child = card, PlacementTarget = meter, Placement = PlacementMode.Right, VerticalOffset = -10,
                AllowsTransparency = true, StaysOpen = false, PopupAnimation = PopupAnimation.Fade,
            };
        }

        /// <summary>One menu line: icon, label, optional hint (hotkey / current value) or check mark; accent bar + glow on hover.</summary>
        private static FrameworkElement MenuRow(string glyph, string label, string? hint, Action act, string? check)
        {
            var glow = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0, Color = (Color)ColorConverter.ConvertFromString(Accent) };
            var icon = new TextBlock { Text = glyph, FontFamily = IconGlyphs, FontSize = 13, Foreground = BrushFrom(IconIdle), VerticalAlignment = VerticalAlignment.Center, Width = 22, Effect = glow };
            var name = new TextBlock { Text = label, FontFamily = ClassicFont, FontSize = 12.5, Foreground = BrushFrom("#e8e8ee"), VerticalAlignment = VerticalAlignment.Center };
            var right = new TextBlock
            {
                Text = check ?? hint ?? "", FontFamily = check is not null ? IconGlyphs : ClassicFont, FontSize = check is not null ? 12 : 10.5,
                Foreground = BrushFrom(check is not null ? Accent : "#7d8190"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 2, 0),
            };
            var bar = new Border { Width = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 4, 6, 4), Background = Brushes.Transparent };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(icon, 1); Grid.SetColumn(name, 2); Grid.SetColumn(right, 3);
            grid.Children.Add(bar); grid.Children.Add(icon); grid.Children.Add(name); grid.Children.Add(right);
            var row = new Border { Padding = new Thickness(2, 5, 8, 5), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Child = grid, Cursor = Cursors.Hand };
            var hover = Frozen(new SolidColorBrush(Color.FromArgb(26, 143, 180, 255)));
            var accent = BrushFrom(Accent);
            row.MouseEnter += (_, _) => { row.Background = hover; bar.Background = accent; icon.Foreground = accent; name.Foreground = Brushes.White; glow.Opacity = 0.9; };
            row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; bar.Background = Brushes.Transparent; icon.Foreground = BrushFrom(IconIdle); name.Foreground = BrushFrom("#e8e8ee"); glow.Opacity = 0; };
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; act(); };
            return row;
        }
    }
}
