using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AionDpsMeter.UI.Views
{
    /// <summary>
    /// Shared look for the meter's pop-up cards (death recap, fight compare, enhancement calculator):
    /// same dark card as the fight summary, placed beside the meter, draggable, ✕ to close.
    /// Cards without inputs never take focus from the game; cards with inputs (<c>activatable</c>) do.
    /// </summary>
    public abstract class CardWindow : Window
    {
        private readonly bool activatable;
        private readonly DispatcherTimer? closeTimer;
        protected readonly double Scale;

        protected CardWindow(string title, Window? near, double overlayScale, bool activatable, TimeSpan? autoClose = null)
        {
            this.activatable = activatable;
            Scale = Math.Clamp(overlayScale, 0.75, 3.0);
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = activatable;
            ShowActivated = activatable;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;

            Opacity = 0;
            ContentRendered += (_, _) =>
            {
                var area = ScreenUtil.WorkAreaOf(near ?? this);
                if (near is null || !near.IsVisible)
                {
                    Left = area.Left + (area.Width - ActualWidth) / 2;
                    Top = area.Top + (area.Height - ActualHeight) / 3;
                }
                else
                {
                    bool right = near.Left + near.ActualWidth + 8 + ActualWidth <= area.Right;
                    Left = right ? near.Left + near.ActualWidth + 8 : near.Left - ActualWidth - 8;
                    Top = near.Top;
                }
                ScreenUtil.KeepInside(this, area);
                Opacity = 1;
            };

            if (autoClose is { } after)
            {
                closeTimer = new DispatcherTimer { Interval = after };
                closeTimer.Tick += (_, _) => { if (IsMouseOver) return; closeTimer.Stop(); Close(); };
                closeTimer.Start();
            }
            Closed += (_, _) => closeTimer?.Stop();
        }

        /// <summary>Wraps the card's content in the shared frame. Call once from the subclass constructor.</summary>
        protected void SetCard(FrameworkElement content, double width)
        {
            if (content is FrameworkElement fe) fe.Width = width;
            var card = new Border
            {
                Padding = new Thickness(14, 11, 14, 11),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(240, 13, 15, 21)),
                BorderBrush = Brush("#40ffffff"),
                BorderThickness = new Thickness(1),
                Child = content,
                LayoutTransform = new ScaleTransform(Scale, Scale),
            };
            // Drag anywhere that isn't a control.
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is TextBox or ComboBox) return;
                try { DragMove(); } catch (InvalidOperationException) { }
            };
            Content = card;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
            if (!activatable) style |= WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, style);
        }

        // ------------------------------------------------------------------ building blocks

        /// <summary>Small caps label, big title, grey subtitle, ✕ on the right.</summary>
        protected FrameworkElement Header(string label, string labelColor, string title, string? subtitle)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(Text(label, 9.5, labelColor, FontWeights.Bold));
            var big = Text(title, 16, "#ffffff", FontWeights.Bold);
            big.TextTrimming = TextTrimming.CharacterEllipsis;
            titles.Children.Add(big);
            if (!string.IsNullOrEmpty(subtitle)) titles.Children.Add(Text(subtitle, 11, "#9aa0b0"));
            grid.Children.Add(titles);
            var close = Text("✕", 12, "#9aa0b0", FontWeights.Bold);
            close.Cursor = Cursors.Hand;
            close.VerticalAlignment = VerticalAlignment.Top;
            close.MouseLeftButtonDown += (_, e) => e.Handled = true; // not a drag
            close.MouseLeftButtonUp += (_, _) => Close();
            Grid.SetColumn(close, 1);
            grid.Children.Add(close);
            return grid;
        }

        protected static TextBlock SectionLabel(string text) =>
            Text(text, 9.5, "#8fb4ff", FontWeights.Bold, new Thickness(0, 10, 0, 4));

        /// <summary>A row of cells with fixed column widths (first column stretches).</summary>
        protected static Grid TableRow(double[] widths, IEnumerable<UIElement> cells, string? background = null)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1), MinHeight = 20 };
            if (background is not null) grid.Background = Brush(background);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foreach (var w in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            int col = 0;
            foreach (var cell in cells)
            {
                if (col > 0 && cell is TextBlock tb) tb.TextAlignment = TextAlignment.Right;
                Grid.SetColumn(cell, col++);
                grid.Children.Add(cell);
            }
            return grid;
        }

        protected static FrameworkElement Button(string label, Action onClick, string? doneLabel = null)
        {
            var text = Text(label, 11, "#e8e8ee", FontWeights.SemiBold);
            var b = new Border
            {
                Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(5),
                Background = Brush("#22ffffff"), BorderBrush = Brush("#40ffffff"), BorderThickness = new Thickness(1),
                Child = text, Cursor = Cursors.Hand,
            };
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, _) =>
            {
                onClick();
                if (doneLabel is null) return;
                text.Text = doneLabel;
                var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                back.Tick += (_, _) => { back.Stop(); text.Text = label; };
                back.Start();
            };
            return b;
        }

        protected static TextBlock Text(string text, double size, string color, FontWeight? weight = null, Thickness? margin = null) => new()
        {
            Text = text, FontSize = size, Foreground = Brush(color), FontWeight = weight ?? FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center, Margin = margin ?? new Thickness(0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        protected static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        protected static string Num(double v) => Math.Round(v).ToString("N0", CultureInfo.CurrentCulture);
        protected static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
