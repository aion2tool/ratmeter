using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.Services.Services.Update;
using AionDpsMeter.UI.Services.Windowing;
using AionDpsMeter.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// Native WPF version of the main DPS meter (replaces the Blazor/WebView2 page,
    /// which needed a whole embedded browser). Same data source: <see cref="MainDpsViewModel"/>.
    /// Row controls are reused between renders, so nothing is rebuilt per tick.
    /// </summary>
    public sealed partial class NativeDpsOverlay : UserControl, IDisposable
    {
        private static readonly FontFamily MonoFont =
            new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#JetBrains Mono, Consolas");

        private static readonly Dictionary<string, (Color From, Color To)> ClassColors = new()
        {
            ["0"] = (Rgba(46, 125, 50, 0.4), Rgba(76, 175, 80, 0.82)),
            ["10"] = (Rgba(0, 96, 100, 0.45), Rgba(38, 198, 218, 0.88)),
            ["11"] = (Rgba(43, 29, 196, 0.42), Rgba(62, 46, 232, 0.86)),
            ["12"] = (Rgba(13, 71, 161, 0.42), Rgba(30, 136, 229, 0.86)),
            ["13"] = (Rgba(27, 94, 32, 0.42), Rgba(76, 175, 80, 0.86)),
            ["14"] = (Rgba(0, 105, 92, 0.42), Rgba(38, 166, 154, 0.86)),
            ["15"] = (Rgba(74, 20, 140, 0.42), Rgba(156, 39, 176, 0.86)),
            ["16"] = (Rgba(0, 96, 100, 0.42), Rgba(38, 198, 218, 0.86)),
            ["17"] = (Rgba(245, 127, 23, 0.42), Rgba(255, 202, 40, 0.86)),
            ["18"] = (Rgba(230, 81, 0, 0.42), Rgba(255, 152, 0, 0.86)),
            ["19"] = (Rgba(183, 28, 28, 0.42), Rgba(244, 67, 54, 0.86)),
        };
        private static readonly (Color, Color) IsMeColors = (Rgba(74, 20, 140, 0.42), Rgba(156, 39, 176, 0.86));
        private static readonly (Color, Color) OtherPlayerColors = (Rgba(27, 94, 32, 0.42), Rgba(76, 175, 80, 0.86));

        private readonly Dictionary<string, Brush> fillBrushCache = new();
        private readonly Dictionary<string, ImageSource?> iconCache = new();

        private readonly IAppSettingsService settingsService;
        private readonly WindowHelper windowHelper;
        private readonly MainDpsViewModel viewModel;

        // Header parts
        private readonly Border header;
        private readonly TextBlock targetName = Text(12.5, "#f2f1f5", FontWeights.Bold);
        private readonly TextBlock petLine = Text(10.5, "#ffd76a", FontWeights.SemiBold);
        private readonly TextBlock pingText = Text(10.5, "#888888", FontWeights.Bold);
        private readonly Rectangle[] pingBars = new Rectangle[3];
        private readonly TextBlock duration = Text(11, "#a1a8b8", FontWeights.Normal);
        private readonly TextBlock targetHp = Text(10.5, "#c9807a", FontWeights.SemiBold);
        private readonly TextBlock totalDps = Text(17, "#4adfc4", FontWeights.ExtraBold);
        private readonly TextBlock headerSecondary = Text(10.5, "#a1a8b8", FontWeights.SemiBold);
        private readonly Grid hpBar;
        private readonly ColumnDefinition hpFilled = new();
        private readonly ColumnDefinition hpEmpty = new();

        // Player rows
        private readonly StackPanel rowsPanel = new();
        private readonly ScaleTransform rowScale = new(1, 1);
        private readonly List<PlayerRow> rows = new();

        private bool disposed;
        private readonly FrameworkElement standardRoot;
        private readonly FrameworkElement standardControls;

        // Every skin is shown inside this host: it applies Overlay Size and hugs the
        // content's height, so empty window space stays transparent (and click-through).
        private readonly Border skinHost = new() { VerticalAlignment = VerticalAlignment.Top };
        private readonly ScaleTransform overlayScale = new(1, 1);
        private double lastOverlayScale = -1;
        private bool cleanMode;
        private readonly List<UIElement> hoverOnly = new();
        private static readonly DropShadowEffect TextShadow = CreateTextShadow();
        // Near-invisible fill: keeps a header draggable/hoverable without showing a panel.
        private static readonly SolidColorBrush GrabBrush = CreateGrabBrush();
        private static readonly SolidColorBrush HeaderBorderBrush = CreateHeaderBorderBrush();
        private bool initialized;
        private double lastAlpha = -1;
        private Brush rowBackground = Brushes.Transparent;

        public NativeDpsOverlay()
        {
            var services = App.AppHost.Services;
            settingsService = services.GetRequiredService<IAppSettingsService>();
            windowHelper = services.GetRequiredService<WindowHelper>();

            viewModel = new MainDpsViewModel(
                services.GetRequiredService<CombatSessionManager>(),
                settingsService,
                services.GetRequiredService<UpdateCheckerService>(),
                services.GetRequiredService<IWindowManagerService>(),
                windowHelper,
                () => { Render(); return Task.CompletedTask; });

            FontFamily = MonoFont;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            header = BuildHeader(out hpBar, out standardControls);
            hoverOnly.Add(standardControls);
            rowsPanel.LayoutTransform = rowScale;

            var root = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(new Border { ClipToBounds = true, Child = rowsPanel });
            standardRoot = root;
            skinHost.LayoutTransform = overlayScale;
            skinHost.Child = root;
            var outer = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            outer.Children.Add(skinHost);
            Content = outer;

            // Re-render when a boss recording starts/stops so the REC badge appears/disappears.
            if (services.GetService(typeof(AionDpsMeter.UI.Services.Recording.BossRecorder)) is AionDpsMeter.UI.Services.Recording.BossRecorder bossRecorder)
                bossRecorder.StateChanged += Render;

            MouseEnter += (_, _) => UpdateHoverControls();
            MouseLeave += (_, _) => UpdateHoverControls();

            Loaded += (_, _) =>
            {
                if (disposed || initialized) return; // Loaded can fire more than once
                initialized = true;
                viewModel.Initialize(); // starts the refresh loop on this (UI) thread
                Render();
            };
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            viewModel.Dispose();
        }

        // ------------------------------------------------------------------ render

        private void Render()
        {
            if (disposed) return;
            ApplyChrome();

            // Skins: Standard / Compact (this one) and Slim (one-line bars, built on first use).
            if (settingsService.UiStyle == SlimStyle) { RenderSlim(); return; }
            ShowSkin(standardRoot);

            double alpha = Math.Clamp(viewModel.PanelAlpha, 0.0, 1.0);
            if (alpha != lastAlpha)
            {
                // Window Opacity only fades panel backgrounds; text stays fully opaque.
                lastAlpha = alpha;
                if (cleanMode)
                {
                    header.Background = GrabBrush;
                }
                else
                {
                    var headerBrush = new LinearGradientBrush(Rgba(32, 34, 38, alpha), Rgba(18, 19, 22, alpha), 90);
                    headerBrush.Freeze();
                    header.Background = headerBrush;
                }
                header.BorderBrush = cleanMode ? Brushes.Transparent : HeaderBorderBrush;
                var rowBrush = new SolidColorBrush(Rgba(0, 0, 0, 0.35 * alpha));
                rowBrush.Freeze();
                rowBackground = rowBrush;
            }

            targetName.Text = string.IsNullOrEmpty(viewModel.ActiveTargetName) ? viewModel.IdleText : viewModel.ActiveTargetName;
            petLine.Text = viewModel.PetLine;
            petLine.Visibility = viewModel.PetLine.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            duration.Text = viewModel.CombatDuration;
            // Big number: total damage of the pull (or DPS, if the user switched it back).
            bool showTotal = settingsService.ShowTotalDamage;
            totalDps.Text = showTotal ? viewModel.TotalDamageFormatted : viewModel.TotalRaidDamageFormatted;
            headerSecondary.Text = showTotal ? viewModel.TotalRaidDamageFormatted : string.Empty;
            targetHp.Text = viewModel.HasActiveTarget && viewModel.ActiveTargetHpDisplay.Length > 0
                ? $"{viewModel.ActiveTargetHpDisplay}   {viewModel.ClampPercent(viewModel.ActiveTargetHpPercentage).ToString("F1", CultureInfo.CurrentCulture)}%"
                : viewModel.ActiveTargetHpDisplay;

            var pingBrush = BrushFrom(viewModel.PingColor);
            pingText.Text = viewModel.PingDisplay;
            pingText.Foreground = pingBrush;
            for (int i = 0; i < pingBars.Length; i++)
            {
                pingBars[i].Fill = pingBrush;
                pingBars[i].Opacity = viewModel.PingLevel >= i + 1 ? 1.0 : 0.35;
            }

            hpBar.Visibility = viewModel.HasActiveTarget ? Visibility.Visible : Visibility.Collapsed;
            double hp = viewModel.ClampPercent(viewModel.ActiveTargetHpPercentage);
            hpFilled.Width = new GridLength(hp, GridUnitType.Star);
            hpEmpty.Width = new GridLength(100 - hp, GridUnitType.Star);

            rowScale.ScaleX = rowScale.ScaleY = viewModel.RowScale;

            var players = viewModel.Players;
            while (rows.Count < players.Count)
            {
                var row = new PlayerRow(this);
                rows.Add(row);
                rowsPanel.Children.Add(row.Root);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                if (i < players.Count)
                {
                    rows[i].Root.Visibility = Visibility.Visible;
                    rows[i].Update(players[i], rowBackground, showTotal);
                }
                else
                {
                    rows[i].Root.Visibility = Visibility.Collapsed;
                }
            }
        }

        // ------------------------------------------------------------------ shared chrome

        /// <summary>Clear-meter hotkey: same as the ⟲ / Clear button.</summary>
        public void ClearFromHotkey() => ClearMeter();

        /// <summary>Height of the visible meter (the window itself can be taller; the rest is transparent).</summary>
        public double ContentHeight => skinHost.ActualHeight * overlayScale.ScaleY;

        private void ShowSkin(FrameworkElement root)
        {
            if (!ReferenceEquals(skinHost.Child, root)) skinHost.Child = root;
        }

        /// <summary>Overlay Size and Background Panel, shared by every skin.</summary>
        private void ApplyChrome()
        {
            double scale = Math.Clamp(settingsService.OverlayScale, 0.75, 3.0);
            if (scale != lastOverlayScale)
            {
                // Grow/shrink the window with the content so the layout keeps its shape.
                if (lastOverlayScale > 0 && Window.GetWindow(this) is { } window)
                {
                    double ratio = scale / lastOverlayScale;
                    var area = SystemParameters.WorkArea;
                    window.Width = Math.Clamp(window.Width * ratio, window.MinWidth, Math.Max(window.MinWidth, area.Width));
                    window.Height = Math.Clamp(window.Height * ratio, window.MinHeight, Math.Max(window.MinHeight, area.Height));
                }
                lastOverlayScale = scale;
                overlayScale.ScaleX = overlayScale.ScaleY = scale;
            }

            bool clean = !settingsService.ShowPanelBackground;
            if (clean != cleanMode)
            {
                cleanMode = clean;
                // Skins re-pick their backgrounds on their next alpha check.
                lastAlpha = slimAlpha = -1;
            }
            // Without a panel behind it, a soft shadow keeps text readable over the game.
            skinHost.Effect = cleanMode ? TextShadow : null;
            UpdateModeButtons();
            UpdateHoverControls();
        }

        /// <summary>Drag the meter window by this element.</summary>
        private void MakeDragHandle(FrameworkElement element)
        {
            element.Cursor = Cursors.SizeAll;
            element.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    viewModel.BeginDrag(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            };
        }

        /// <summary>Mode switch, Settings and Close stay visible in clean mode; the rest appear on hover.</summary>
        private static bool AlwaysVisible(FrameworkElement? e) =>
            e is not null && (ReferenceEquals(e.Tag, ModeTag) || e.ToolTip is "Settings" or "Close");

        private void UpdateHoverControls()
        {
            double opacity = !cleanMode || IsMouseOver ? 1.0 : 0.0;
            foreach (var element in hoverOnly)
            {
                if (element is Panel panel)
                {
                    foreach (UIElement child in panel.Children)
                        child.Opacity = AlwaysVisible(child as FrameworkElement) ? 1.0 : opacity;
                }
                else
                {
                    element.Opacity = opacity;
                }
            }
        }

        private static DropShadowEffect CreateTextShadow()
        {
            var effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Direction = 315, Opacity = 0.95 };
            effect.Freeze();
            return effect;
        }

        private static SolidColorBrush CreateGrabBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(3, 0, 0, 0));
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush CreateHeaderBorderBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(33, 255, 255, 255));
            brush.Freeze();
            return brush;
        }

        // ------------------------------------------------------------------ header

        private Border BuildHeader(out Grid hpTrack, out FrameworkElement headerControls)
        {
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            targetName.TextTrimming = TextTrimming.CharacterEllipsis;
            targetName.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(targetName);

            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 3) };
            for (int i = 0; i < pingBars.Length; i++)
            {
                pingBars[i] = new Rectangle { Width = 2, Height = 4 + i * 3, Margin = new Thickness(0, 0, 1, 0), VerticalAlignment = VerticalAlignment.Bottom };
                bars.Children.Add(pingBars[i]);
            }
            controls.Children.Add(bars);
            pingText.Margin = new Thickness(0, 0, 10, 0);
            pingText.VerticalAlignment = VerticalAlignment.Center;
            controls.Children.Add(pingText);
            duration.Margin = new Thickness(0, 0, 8, 0);
            duration.VerticalAlignment = VerticalAlignment.Center;
            controls.Children.Add(duration);
            // Buttons only (not ping/timer): these fade out in clean mode until hovered.
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(ModeButton()); // chips, mode, lock and the ⋯ menu (NativeDpsOverlay.Header.cs)
            controls.Children.Add(buttons);
            headerControls = buttons;
            Grid.SetColumn(controls, 1);
            top.Children.Add(controls);

            var bottom = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            targetHp.VerticalAlignment = VerticalAlignment.Center;
            bottom.Children.Add(targetHp);
            var bigNumbers = new StackPanel { Orientation = Orientation.Horizontal };
            headerSecondary.VerticalAlignment = VerticalAlignment.Center;
            headerSecondary.Margin = new Thickness(0, 0, 8, 0);
            bigNumbers.Children.Add(headerSecondary);
            bigNumbers.Children.Add(totalDps);
            Grid.SetColumn(bigNumbers, 1);
            bottom.Children.Add(bigNumbers);

            hpTrack = new Grid { Height = 4, Margin = new Thickness(0, 6, 0, 0), ClipToBounds = true };
            hpTrack.ColumnDefinitions.Add(hpFilled);
            hpTrack.ColumnDefinitions.Add(hpEmpty);
            var track = new Border { Background = BrushFrom("#4d4d4d"), CornerRadius = new CornerRadius(2) };
            Grid.SetColumnSpan(track, 2);
            hpTrack.Children.Add(track);
            var fill = new Border
            {
                CornerRadius = new CornerRadius(2),
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(BrushColor("#7a2320"), 0),
                    new GradientStop(BrushColor("#e0524a"), 0.6),
                    new GradientStop(BrushColor("#ff6f63"), 1),
                }, 0),
            };
            hpTrack.Children.Add(fill);

            var stack = new StackPanel();
            stack.Children.Add(top);
            // Pet souls: shown only while the target feeds a pet that isn't maxed (Pet Tracker).
            petLine.Margin = new Thickness(0, 2, 0, 0);
            petLine.TextTrimming = TextTrimming.CharacterEllipsis;
            petLine.Visibility = Visibility.Collapsed;
            stack.Children.Add(petLine);
            stack.Children.Add(bottom);
            stack.Children.Add(hpTrack);

            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 7, 8, 7),
                Margin = new Thickness(0, 0, 0, 6),
                BorderBrush = HeaderBorderBrush,
                BorderThickness = new Thickness(1),
                Child = stack,
                Cursor = Cursors.SizeAll,
            };
            // Drag the whole meter by its header, like the original.
            border.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    viewModel.BeginDrag(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            };
            return border;
        }

        // ------------------------------------------------------------------ rows

        private Brush GetFillBrush(PlayerRenderState player)
        {
            string key = ColorKey(player);
            if (fillBrushCache.TryGetValue(key, out var cached)) return cached;

            var (from, to) = GetBarColors(player);
            var brush = new LinearGradientBrush(from, to, 0);
            brush.Freeze();
            fillBrushCache[key] = brush;
            return brush;
        }

        private ImageSource? GetClassIcon(string classId)
        {
            if (iconCache.TryGetValue(classId, out var cached)) return cached;
            ImageSource? image = null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                // Icons under wwwroot are Blazor static web assets: WPF resolves them as loose files
                // next to the exe (which don't exist), so use the embedded Assets/Classes icons that
                // the game data points to (e.g. "/Assets/Classes/templar.png").
                string? path = int.TryParse(classId, out var id)
                    ? AionDpsMeter.Core.GameData.Services.GameDataProvider.Instance.Classes.GetById(id)?.Icon
                    : null;
                if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("no icon for this class id");
                bmp.UriSource = new Uri($"pack://application:,,,/AionDpsMeter.UI;component{path}");
                bmp.DecodePixelWidth = 60;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                image = bmp;
            }
            catch (Exception ex)
            {
                // Unknown class id: show no icon rather than fail the whole row (logged once per id).
                App.AppHost.Services.GetService<ILogger<NativeDpsOverlay>>()?
                    .LogWarning("Class icon {ClassId} failed to load: {Error}", classId, ex.Message);
            }
            iconCache[classId] = image;
            return image;
        }

        private sealed class PlayerRow
        {
            private readonly NativeDpsOverlay owner;
            private PlayerRenderState? player;

            public Border Root { get; }
            private readonly ColumnDefinition filled = new();
            private readonly ColumnDefinition empty = new();
            private readonly Border fill = new() { Opacity = 0.55, CornerRadius = new CornerRadius(7, 0, 0, 7) };
            private readonly Image icon = new() { Width = 30, Height = 30, Clip = new RectangleGeometry(new Rect(0, 0, 30, 30), 6, 6) };
            private readonly TextBlock combatPower = Text(10.5, "#ffd76a", FontWeights.Bold);
            private readonly Border combatPowerPill = new()
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 10, 11, 14)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 0, 4, 1),
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            private readonly TextBlock name = Text(11.5, "#f2f1f5", FontWeights.Bold);
            private readonly TextBlock critValue = Text(10, "#dfe3ee", FontWeights.SemiBold);
            private readonly TextBlock deaths = Text(10, "#ff8a8c", FontWeights.Bold);
            private readonly TextBlock dps = Text(11.5, "#f0eff5", FontWeights.Bold);
            private readonly TextBlock pct = Text(12, "#ffffff", FontWeights.Bold);
            private readonly TextBlock damage = Text(10, "#e2e6f0", FontWeights.SemiBold);
            private readonly TextBlock unit = Text(9, "#99ffffff", FontWeights.Medium);

            public PlayerRow(NativeDpsOverlay owner)
            {
                this.owner = owner;

                var fillGrid = new Grid();
                fillGrid.ColumnDefinitions.Add(filled);
                fillGrid.ColumnDefinitions.Add(empty);
                fillGrid.Children.Add(fill);

                var iconCol = new StackPanel { Width = 50, VerticalAlignment = VerticalAlignment.Center };
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                // Gear score: gold on a dark pill so it reads over any bar colour.
                combatPowerPill.Child = combatPower;
                iconCol.Children.Add(icon);
                iconCol.Children.Add(combatPowerPill);

                var info = new Grid { Margin = new Thickness(0, 5, 10, 5) };
                info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                info.RowDefinitions.Add(new RowDefinition());
                info.RowDefinitions.Add(new RowDefinition());

                name.TextTrimming = TextTrimming.CharacterEllipsis;
                info.Children.Add(name);

                var dpsLine = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                unit.Text = "/s";
                unit.VerticalAlignment = VerticalAlignment.Bottom;
                unit.Margin = new Thickness(0, 0, 7, 1);
                dpsLine.Children.Add(dps);
                dpsLine.Children.Add(unit);
                pct.MinWidth = 38;
                pct.TextAlignment = TextAlignment.Right;
                dpsLine.Children.Add(pct);
                Grid.SetColumn(dpsLine, 1);
                info.Children.Add(dpsLine);

                var subLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var critLabel = Text(9, "#aab0c2", FontWeights.Bold);
                critLabel.Text = "CRIT ";
                critValue.Margin = new Thickness(0, 0, 6, 0);
                subLine.Children.Add(critLabel);
                subLine.Children.Add(critValue);
                subLine.Children.Add(deaths);
                Grid.SetRow(subLine, 1);
                info.Children.Add(subLine);

                damage.HorizontalAlignment = HorizontalAlignment.Right;
                damage.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(damage, 1);
                Grid.SetColumn(damage, 1);
                info.Children.Add(damage);

                var content = new Grid();
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.Children.Add(iconCol);
                Grid.SetColumn(info, 1);
                content.Children.Add(info);

                var layers = new Grid();
                layers.Children.Add(fillGrid);
                layers.Children.Add(content);

                Root = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Margin = new Thickness(0, 0, 0, 5),
                    BorderBrush = new SolidColorBrush(Rgba(255, 255, 255, 0.06)),
                    BorderThickness = new Thickness(1),
                    Child = layers,
                    ClipToBounds = true,
                    Cursor = Cursors.Hand,
                };
                Root.MouseLeftButtonUp += (_, _) =>
                {
                    if (player is not null) owner.viewModel.OpenGearProfile(player);
                };
                Root.MouseRightButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (player is not null) owner.viewModel.OpenPlayerDetails(player);
                };
                Root.ToolTip ??= "Click: gear profile · Right-click: skills";
            }

            public void Update(PlayerRenderState p, Brush background, bool showTotal)
            {
                player = p;
                owner.SetSelfHighlight(Root, p);
                Root.Background = background;

                double width = owner.viewModel.ClampPercent(p.EffectivePercentage);
                filled.Width = new GridLength(width, GridUnitType.Star);
                empty.Width = new GridLength(100 - width, GridUnitType.Star);
                owner.PaintBar(fill, p);
                fill.BorderBrush = p.IsUser && owner.settingsService.UseClassColors ? Brushes.White : null;
                fill.BorderThickness = new Thickness(p.IsUser && owner.settingsService.UseClassColors ? 2 : 0, 0, 0, 0);

                icon.Source = owner.GetClassIcon(p.ClassId);
                var score = owner.viewModel.GetCombatScoreDisplay(p);
                combatPower.Text = score;
                combatPowerPill.Visibility = string.IsNullOrEmpty(score) ? Visibility.Collapsed : Visibility.Visible;

                name.Text = p.PlayerNameDisplay;
                name.ToolTip = p.PlayerNameDisplay;
                critValue.Text = p.CriticalRate.ToString("F1", CultureInfo.CurrentCulture) + "%";
                deaths.Text = p.DeathsDisplay;
                // showTotal: total damage is the big number, DPS the small line under it.
                dps.Text = showTotal ? p.TotalDamageFormatted : p.DpsFormatted;
                unit.Visibility = showTotal ? Visibility.Collapsed : Visibility.Visible;
                if (showTotal) dps.Margin = new Thickness(0, 0, 7, 0); else dps.Margin = new Thickness(0);
                pct.Text = p.DamagePercentage.ToString("F1", CultureInfo.CurrentCulture) + "%";
                damage.Text = showTotal ? p.DpsFormatted + "/s" : p.TotalDamageFormatted;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static TextBlock Text(double size, string color, FontWeight weight) => new()
        {
            FontSize = size,
            FontWeight = weight,
            Foreground = BrushFrom(color),
        };

        private static Color Rgba(byte r, byte g, byte b, double a) =>
            Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), r, g, b);

        private static Color BrushColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static readonly Dictionary<string, SolidColorBrush> solidCache = new();

        private static SolidColorBrush BrushFrom(string hex)
        {
            if (solidCache.TryGetValue(hex, out var b)) return b;
            b = new SolidColorBrush(BrushColor(hex));
            b.Freeze();
            solidCache[hex] = b;
            return b;
        }
    }
}
