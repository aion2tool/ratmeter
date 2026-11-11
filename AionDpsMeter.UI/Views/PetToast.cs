using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AionDpsMeter.Services.Services.Pets;

namespace AionDpsMeter.UI.Views
{
    /// <summary>"🐾 PET LEVEL 2!" / "🐾 PET MAXED!" card, bottom-right of the game screen for 10 s. Never takes focus.</summary>
    public sealed class PetToast : Window
    {
        private const string Accent = "#ffd76a";
        private readonly Rect screen;

        public static void Show(PetLevelUp up, double overlayScale)
        {
            var meter = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            var screen = ScreenUtil.GameWorkArea(meter is { IsVisible: true } && meter.WindowState != WindowState.Minimized ? meter : null);
            new PetToast(up, screen, overlayScale).Show();
        }

        private PetToast(PetLevelUp up, Rect gameScreen, double overlayScale)
        {
            screen = gameScreen;
            Title = "AION2 Pet";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = new FontFamily("Segoe UI");

            var stack = new StackPanel { Width = 260 };
            stack.Children.Add(Text(up.Level >= PetLevels.Max ? "🐾 PET MAXED!" : $"🐾 PET LEVEL {up.Level}!", 11, Accent, FontWeights.Bold));
            var name = Text(up.Pet, 14, "#ffffff", FontWeights.Bold);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(name);
            stack.Children.Add(Text(up.Level >= PetLevels.Max ? "All 105 souls" : $"Next: 0 / {PetLevels.NeedFor(up.Level)}", 10.5, "#a9adbd", FontWeights.Normal));

            double scale = Math.Clamp(overlayScale, 0.75, 3.0);
            var card = new Border
            {
                Margin = new Thickness(8), Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Accent)),
                Background = new SolidColorBrush(Color.FromArgb(242, 13, 15, 21)), Child = stack,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.7, Color = (Color)ColorConverter.ConvertFromString(Accent) },
                LayoutTransform = new ScaleTransform(scale, scale),
            };
            card.MouseLeftButtonUp += (_, _) => Close();
            Content = card;

            var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            closeTimer.Tick += (_, _) => { if (!IsMouseOver) Close(); };
            Closed += (_, _) => closeTimer.Stop();
            SizeChanged += (_, _) => Place();
            Loaded += (_, _) =>
            {
                Place();
                var move = new TranslateTransform(300, 0);
                card.RenderTransform = move;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(300, 0, TimeSpan.FromMilliseconds(350)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                closeTimer.Start();
            };
        }

        /// <summary>Bottom-right of the game screen, above the boss alerts and the fight result card when showing.</summary>
        private void Place()
        {
            double above = Application.Current.Windows.OfType<BossAlertWindow>().FirstOrDefault(w => w.IsVisible)?.ActualHeight ?? 0;
            above += Application.Current.Windows.OfType<FightResultToast>().FirstOrDefault(w => w.IsVisible)?.ActualHeight ?? 0;
            Left = screen.Right - ActualWidth - 8;
            Top = screen.Bottom - ActualHeight - 16 - above;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        private static TextBlock Text(string text, double size, string color, FontWeight weight) => new()
        {
            Text = text, FontSize = size, FontWeight = weight,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
        };

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
