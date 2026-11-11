using System.Windows;
using AionDpsMeter.UI.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// MOB / ALL / BOSS switch shown in every skin's header. One click cycles
    /// mob by mob → all mobs in the pull → boss only. Stays visible in clean mode.
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        private static readonly object ModeTag = new();
        private readonly List<(Border Button, TextBlock Label)> modeButtons = new();

        private static readonly (string Label, string Color, string Tip)[] Modes =
        {
            ("MOB", "#8fb4ff", "Mob by mob: shows the mob you're hitting, resets per mob"),
            ("ALL", "#4adfc4", "All mobs: adds up every mob in the pull, resets after a gap"),
            ("BOSS", "#ff6f63", "Boss only: shows just the boss fight (adds are still tracked)"),
        };


        private int CurrentMode => settingsService.AllTargetsMode ? 1 : settingsService.BossFocus ? 2 : 0;

        private readonly List<(Border Badge, TextBlock Dot, TextBlock Label)> recBadges = new();
        private AionDpsMeter.UI.Services.Recording.BossRecorder? recorder;
        private System.Windows.Threading.DispatcherTimer? recTick;
        private bool recPulsing;

        /// <summary>REC button (click to start / stop a recording; red, blinking and timing while recording) + the mode switch.</summary>
        private FrameworkElement ModeButton()
        {
            recorder ??= App.AppHost.Services.GetService(typeof(AionDpsMeter.UI.Services.Recording.BossRecorder)) as AionDpsMeter.UI.Services.Recording.BossRecorder;
            var recDot = ClassicText("●", 13, "#ff4040", FontWeights.Bold);
            recDot.VerticalAlignment = VerticalAlignment.Center;
            recDot.Margin = new Thickness(0, 0, 4, 0);
            var recLabel = ClassicText("RECORD", 11, "#ff6a6a", FontWeights.Bold);
            recLabel.VerticalAlignment = VerticalAlignment.Center;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(recDot);
            content.Children.Add(recLabel);
            var badge = new Border
            {
                Height = 20,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(7, 0, 8, 0),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = content,
                Cursor = Cursors.Hand,
                Visibility = recorder is null ? Visibility.Collapsed : Visibility.Visible,
            };
            badge.MouseLeftButtonDown += (_, e) => e.Handled = true; // don't start a drag
            badge.MouseLeftButtonUp += (_, e) => { e.Handled = true; recorder?.ToggleManual(); UpdateRecBadges(); };
            recBadges.Add((badge, recDot, recLabel));

            var container = new StackPanel { Orientation = Orientation.Horizontal, Tag = ModeTag, VerticalAlignment = VerticalAlignment.Center };
            container.Children.Add(BestChip());
            container.Children.Add(LagChip());
            container.Children.Add(badge);
            container.Children.Add(ModeSwitch());
            container.Children.Add(LockButton());
            container.Children.Add(MoreButton()); // everything else lives in the ⋯ menu
            UpdateRecBadges();
            return container;
        }

        /// <summary>
        /// REC button state: solid red "● 1:23 ■ STOP" with a blinking dot while recording, grey "● REC" when idle,
        /// "● REC ✕" with the error as tooltip when the last start failed.
        /// </summary>
        private void UpdateRecBadges()
        {
            if (recorder is null) return;
            bool recording = recorder.IsRecording;
            string? error = recorder.LastError;
            string hotkey = settingsService.RecordHotkey;
            foreach (var (badge, dot, label) in recBadges)
            {
                if (recording)
                {
                    var t = recorder.Elapsed;
                    label.Text = $"{(int)t.TotalHours * 60 + t.Minutes}:{t.Seconds:00}  ■ STOP";
                    label.Foreground = Brushes.White;
                    dot.Foreground = Brushes.White;
                    badge.Background = BrushFrom("#e02828");
                    badge.BorderBrush = BrushFrom("#ff7070");
                    badge.ToolTip = $"Recording your screen. Click (or {hotkey}) to stop and save it";
                }
                else
                {
                    label.Text = error is null ? "RECORD" : "REC ✕";
                    label.Foreground = BrushFrom(error is null ? "#ff6a6a" : "#c9ccd6");
                    dot.Foreground = BrushFrom(error is null ? "#ff4040" : "#9a9eab");
                    badge.Background = BrushFrom(error is null ? "#40ff2020" : "#22ffffff");
                    badge.BorderBrush = BrushFrom(error is null ? "#ccff4040" : "#44ffffff");
                    badge.ToolTip = error is null ? $"Click (or {hotkey}) to start recording your screen" : $"Last recording failed: {error}\nClick to try again";
                }
            }
            SetRecPulse(recording);
        }

        /// <summary>While recording: the dot blinks and the timer counts every second.</summary>
        private void SetRecPulse(bool on)
        {
            if (on == recPulsing) return;
            recPulsing = on;
            foreach (var (_, dot, _) in recBadges)
            {
                if (on)
                    dot.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0.15, TimeSpan.FromMilliseconds(600))
                    { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
                else
                {
                    dot.BeginAnimation(OpacityProperty, null);
                    dot.Opacity = 1;
                }
            }
            if (on)
            {
                recTick ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(1), System.Windows.Threading.DispatcherPriority.Background,
                    (_, _) => { if (disposed) recTick?.Stop(); else UpdateRecBadges(); }, Dispatcher);
                recTick.Start();
            }
            else recTick?.Stop();
        }

        private Border ModeSwitch()
        {
            var label = ClassicText("", 9.5, "#ffffff", FontWeights.Bold);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            var button = new Border
            {
                Tag = ModeTag,
                MinWidth = 36,
                Height = 16,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(5, 0, 5, 0),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = label,
                Cursor = Cursors.Hand,
            };
            button.MouseLeftButtonDown += (_, e) => e.Handled = true; // don't start a drag
            button.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                int next = (CurrentMode + 1) % Modes.Length;
                settingsService.AllTargetsMode = next == 1;
                settingsService.BossFocus = next == 2;
            };
            modeButtons.Add((button, label));
            StyleModeButton(button, label);
            return button;
        }

        private int styledMode = -1;

        private void UpdateModeButtons()
        {
            UpdateRecBadges();
            // Called every render: only restyle when the mode actually changed.
            if (CurrentMode == styledMode) return;
            styledMode = CurrentMode;
            foreach (var (button, label) in modeButtons) StyleModeButton(button, label);
        }

        private void StyleModeButton(Border button, TextBlock label)
        {
            var (text, color, tip) = Modes[CurrentMode];
            var c = BrushColor(color);
            label.Text = text;
            label.Foreground = BrushFrom(color);
            button.Background = Frozen(new SolidColorBrush(Color.FromArgb(46, c.R, c.G, c.B)));
            button.BorderBrush = Frozen(new SolidColorBrush(Color.FromArgb(140, c.R, c.G, c.B)));
            button.ToolTip = tip + "\nClick to switch: Mob → All → Boss";
        }
    }
}
