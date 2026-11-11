using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDpsMeter.UI.ViewModels;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// "Slim" skin: one header line (timer, target, HP %) and thin one-line bars with
    /// rank, class icon, name, main number and damage share. Takes the least screen space.
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        private const int SlimStyle = 5;

        private FrameworkElement? slimRoot;
        private TextBlock? slimTime;
        private TextBlock? slimTarget;
        private TextBlock? slimHpPct;
        private Grid? slimHpBar;
        private readonly ColumnDefinition slimHpFilled = new();
        private readonly ColumnDefinition slimHpEmpty = new();
        private readonly StackPanel slimRows = new() { Margin = new Thickness(2, 3, 2, 0) };
        private readonly List<SlimRow> slimRowPool = new();
        private double slimAlpha = -1;
        private Brush slimTrackBrush = Brushes.Transparent;
        private Border? slimFrame;

        private void RenderSlim()
        {
            slimRoot ??= BuildSlim();
            ShowSkin(slimRoot);

            double alpha = Math.Clamp(viewModel.PanelAlpha, 0.0, 1.0);
            if (alpha != slimAlpha)
            {
                slimAlpha = alpha;
                slimFrame!.Background = cleanMode ? Brushes.Transparent : Frozen(new SolidColorBrush(Rgba(18, 19, 24, alpha)));
                slimTrackBrush = Frozen(new SolidColorBrush(Rgba(8, 8, 12, (cleanMode ? 0.45 : 0.6) * Math.Max(alpha, 0.5))));
            }

            bool showTotal = settingsService.ShowTotalDamage;
            SetTextWithPing(slimTime!, viewModel.CombatDuration);
            slimTarget!.Text = string.IsNullOrEmpty(viewModel.ActiveTargetName) ? viewModel.IdleText : viewModel.ActiveTargetName;
            double hp = viewModel.ClampPercent(viewModel.ActiveTargetHpPercentage);
            bool hasTarget = viewModel.HasActiveTarget;
            slimHpPct!.Text = hasTarget ? hp.ToString("F1", CultureInfo.CurrentCulture) + "%" : (showTotal ? viewModel.TotalDamageFormatted : viewModel.TotalRaidDamageFormatted);
            slimHpBar!.Visibility = hasTarget ? Visibility.Visible : Visibility.Collapsed;
            slimHpFilled.Width = new GridLength(hp, GridUnitType.Star);
            slimHpEmpty.Width = new GridLength(100 - hp, GridUnitType.Star);

            var players = viewModel.Players;
            while (slimRowPool.Count < players.Count)
            {
                var row = new SlimRow(this);
                slimRowPool.Add(row);
                slimRows.Children.Add(row.Root);
            }
            for (int i = 0; i < slimRowPool.Count; i++)
            {
                if (i < players.Count)
                {
                    slimRowPool[i].Root.Visibility = Visibility.Visible;
                    slimRowPool[i].Update(players[i], i + 1, slimTrackBrush, showTotal);
                }
                else
                {
                    slimRowPool[i].Root.Visibility = Visibility.Collapsed;
                }
            }
        }

        private FrameworkElement BuildSlim()
        {
            var header = new Grid { Background = GrabBrush, Cursor = Cursors.SizeAll, Margin = new Thickness(4, 2, 2, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            slimTime = ClassicText("", 11, "#8fb4ff", FontWeights.SemiBold);
            slimTime.Margin = new Thickness(0, 0, 8, 0);
            slimTime.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(slimTime);

            slimTarget = ClassicText("", 11.5, "#ffffff", FontWeights.Bold);
            slimTarget.TextTrimming = TextTrimming.CharacterEllipsis;
            slimTarget.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(slimTarget, 1);
            header.Children.Add(slimTarget);

            slimHpPct = ClassicText("", 11, PartyGold, FontWeights.Bold);
            slimHpPct.Margin = new Thickness(8, 0, 4, 0);
            slimHpPct.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(slimHpPct, 2);
            header.Children.Add(slimHpPct);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(ModeButton());
            Grid.SetColumn(buttons, 3);
            header.Children.Add(buttons);
            hoverOnly.Add(buttons);
            header.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    viewModel.BeginDrag(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            };

            slimHpBar = new Grid { Height = 2, Margin = new Thickness(4, 2, 4, 0) };
            slimHpBar.ColumnDefinitions.Add(slimHpFilled);
            slimHpBar.ColumnDefinitions.Add(slimHpEmpty);
            var track = new Border { Background = BrushFrom("#40ffffff") };
            Grid.SetColumnSpan(track, 2);
            slimHpBar.Children.Add(track);
            slimHpBar.Children.Add(new Border { Background = BrushFrom("#e0524a") });

            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(slimHpBar);
            slimRows.LayoutTransform = rowScale;
            stack.Children.Add(slimRows);

            slimFrame = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(2, 2, 2, 3), Child = stack };
            return slimFrame;
        }

        private sealed class SlimRow
        {
            private readonly NativeDpsOverlay owner;
            private PlayerRenderState? player;

            public Border Root { get; }
            private readonly ColumnDefinition filled = new();
            private readonly ColumnDefinition empty = new();
            private readonly Border fill = new() { Opacity = 0.95, CornerRadius = new CornerRadius(3) };
            private readonly TextBlock rank = ClassicText("", 10, "#b3ffffff", FontWeights.Bold);
            private readonly Image icon = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0) };
            private readonly TextBlock name = ClassicText("", 11.5, "#ffffff", FontWeights.SemiBold);
            private readonly TextBlock main = ClassicText("", 11.5, "#ffffff", FontWeights.Bold);
            private readonly TextBlock rate = ClassicText("", 10.5, DpsRateColor, FontWeights.Bold);
            private readonly TextBlock pct = ClassicText("", 10.5, PartyGold, FontWeights.Bold);

            public SlimRow(NativeDpsOverlay owner)
            {
                this.owner = owner;

                var fillGrid = new Grid();
                fillGrid.ColumnDefinitions.Add(filled);
                fillGrid.ColumnDefinitions.Add(empty);
                fillGrid.Children.Add(fill);

                var content = new Grid { Height = 21 };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                rank.HorizontalAlignment = HorizontalAlignment.Center;
                rank.VerticalAlignment = VerticalAlignment.Center;
                content.Children.Add(rank);
                icon.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(icon, 1);
                content.Children.Add(icon);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(name, 2);
                content.Children.Add(name);
                main.VerticalAlignment = VerticalAlignment.Center;
                main.Margin = new Thickness(6, 0, 8, 0);
                Grid.SetColumn(main, 3);
                content.Children.Add(main);
                rate.VerticalAlignment = VerticalAlignment.Center;
                rate.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(rate, 4);
                content.Children.Add(rate);
                pct.VerticalAlignment = VerticalAlignment.Center;
                pct.MinWidth = 40;
                pct.TextAlignment = TextAlignment.Right;
                pct.Margin = new Thickness(0, 0, 6, 0);
                Grid.SetColumn(pct, 5);
                content.Children.Add(pct);

                var layers = new Grid();
                layers.Children.Add(fillGrid);
                layers.Children.Add(content);

                Root = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 0, 2),
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

            public void Update(PlayerRenderState p, int position, Brush track, bool showTotal)
            {
                player = p;
                owner.SetSelfHighlight(Root, p);
                Root.Background = track;

                double width = owner.viewModel.ClampPercent(p.EffectivePercentage);
                filled.Width = new GridLength(width, GridUnitType.Star);
                empty.Width = new GridLength(100 - width, GridUnitType.Star);
                owner.PaintBar(fill, p);

                rank.Text = position.ToString(CultureInfo.CurrentCulture);
                icon.Source = owner.GetClassIcon(p.ClassId);
                name.Text = p.PlayerNameDisplay + (string.IsNullOrEmpty(p.DeathsDisplay) ? "" : "  " + p.DeathsDisplay);
                name.ToolTip = p.PlayerNameDisplay;
                main.Text = showTotal ? CommaNumber(p.TotalDamage) : CommaNumber(p.Dps) + "/s";
                main.ToolTip = showTotal ? "DPS " + CommaNumber(p.Dps) + "/s" : "Total " + CommaNumber(p.TotalDamage);
                SetRate(rate, p, showTotal);
                pct.Text = p.DamagePercentage.ToString("F1", CultureInfo.CurrentCulture) + "%";
            }
        }
    }
}
