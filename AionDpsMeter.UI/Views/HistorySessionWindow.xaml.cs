using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.UI.ViewModels;
using AionDpsMeter.UI.ViewModels.History;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace AionDpsMeter.UI
{
    public partial class HistorySessionWindow : Window
    {
        private readonly IAppSettingsService _settingsService;

        public HistorySessionWindow(IAppSettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void RecordingButton_Click(object sender, RoutedEventArgs e) => OpenRecording();

        private void CompareButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { ContextMenu: { } menu } button)
            {
                menu.PlacementTarget = button;
                menu.IsOpen = true;
            }
        }

        private void CompareBest_Click(object sender, RoutedEventArgs e) =>
            Compare((svc, id) => svc.CompareWithBest(id, this));

        private void ComparePrevious_Click(object sender, RoutedEventArgs e) =>
            Compare((svc, id) => svc.CompareWithPrevious(id, this));

        private void Compare(Func<AionDpsMeter.UI.Services.Records.FightCompareService, Guid, string?> open)
        {
            if (DataContext is not HistorySessionViewModel session) return;
            var service = App.AppHost.Services.GetService(typeof(AionDpsMeter.UI.Services.Records.FightCompareService))
                as AionDpsMeter.UI.Services.Records.FightCompareService;
            if (service is null) return;
            if (open(service, session.SessionId) is { } problem)
                MessageBox.Show(this, problem, "Fight compare", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PlayerItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el &&
                el.Tag is HistoryPlayerViewModel player &&
                DataContext is HistorySessionViewModel session)
            {
                var vm = PlayerDetailsViewModel.FromSnapshot(player, _settingsService, session.TargetName);
                var detailsWindow = new PlayerDetailsWindow
                {
                    DataContext = vm,
                    Owner = this
                };
                detailsWindow.Show();
            }
        }

        public void OpenRecording()
        {
            if (DataContext is not HistorySessionViewModel session) return;

            var folder = _settingsService.RecorderFolder;
            var recording = Directory.Exists(folder) ? FindRecording(folder, session.SessionStart, session.SessionEnd) : null;
            if (recording is null)
            {
                MessageBox.Show(this, "There is no video of this fight. Videos are made for boss fights while Settings → Recording → Record Boss Fights is on, and old ones are deleted automatically.",
                    "No recording", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(recording) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open recording: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Video names start with the recording's start time ("2026-10-08 15-29-02 Boss (kill, 0m47s).mp4"); pre-roll starts it a little early.</summary>
        private static string? FindRecording(string folder, DateTime start, DateTime end)
        {
            string? best = null;
            double bestGap = double.MaxValue;
            foreach (var file in Directory.EnumerateFiles(folder, "*.mp4"))
            {
                var name = Path.GetFileName(file);
                if (name.Length < 19 || !DateTime.TryParseExact(name[..19], "yyyy-MM-dd HH-mm-ss",
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at)) continue;
                if (at < start.AddMinutes(-2) || at > end) continue;
                double gap = Math.Abs((at - start).TotalSeconds);
                if (gap < bestGap) { bestGap = gap; best = file; }
            }
            return best;
        }
    }
}
