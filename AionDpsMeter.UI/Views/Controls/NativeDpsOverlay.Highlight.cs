using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AionDpsMeter.UI.ViewModels;

namespace AionDpsMeter.UI.Views.Controls
{
    /// <summary>
    /// "That's me" highlight: when more than one player is on the meter, your own row gets a thin gold
    /// outline and a soft light band that sweeps across it every few seconds. Drawn as an adorner on top
    /// of the row, so it works the same in every skin.
    /// </summary>
    public sealed partial class NativeDpsOverlay
    {
        private readonly Dictionary<UIElement, ShimmerAdorner> shimmers = new();

        /// <summary>Called from each skin's row update.</summary>
        private void SetSelfHighlight(UIElement row, PlayerRenderState p)
        {
            // Not while the meter is faded out: the adorner layer isn't covered by the fade.
            bool want = settingsService.HighlightSelf && p.IsUser && viewModel.Players.Count > 1 && row.Visibility == Visibility.Visible && !autoHidden;
            if (want == shimmers.ContainsKey(row)) return;

            var layer = AdornerLayer.GetAdornerLayer(row);
            if (layer is null) return; // not in the visual tree yet; next update tries again
            if (want)
            {
                var adorner = new ShimmerAdorner(row);
                layer.Add(adorner);
                shimmers[row] = adorner;
            }
            else
            {
                layer.Remove(shimmers[row]);
                shimmers.Remove(row);
            }
        }

        private sealed class ShimmerAdorner : Adorner
        {
            private static readonly Pen Outline = CreateOutline();
            private readonly TranslateTransform sweep = new();
            private readonly LinearGradientBrush band;

            public ShimmerAdorner(UIElement row) : base(row)
            {
                IsHitTestVisible = false; // clicks still go to the row
                band = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0.35),
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.38),
                        new GradientStop(Color.FromArgb(70, 255, 240, 200), 0.5),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.62),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0),
                    },
                    RelativeTransform = sweep,
                };
                // Band travels from off the left edge to off the right edge, then pauses.
                var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(3.2) };
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(-1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.4))) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.2))));
                sweep.BeginAnimation(TranslateTransform.XProperty, animation);
            }

            protected override void OnRender(DrawingContext dc)
            {
                var size = AdornedElement.RenderSize;
                if (size.Width <= 0 || size.Height <= 0) return;
                var rect = new Rect(0.5, 0.5, size.Width - 1, size.Height - 1);
                double radius = Math.Min(8, size.Height / 2);
                dc.PushClip(new RectangleGeometry(rect, radius, radius));
                dc.DrawRectangle(band, null, rect);
                dc.Pop();
                dc.DrawRoundedRectangle(null, Outline, rect, radius, radius);
            }

            private static Pen CreateOutline()
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 242, 193, 78)), 1.2);
                pen.Freeze();
                return pen;
            }
        }
    }
}
