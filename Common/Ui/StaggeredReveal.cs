using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LinuxHub.Common.Ui
{
    /// <summary>
    /// Cascading fade/slide reveal for detail panels — elements appear one after another.
    /// </summary>
    public static class StaggeredReveal
    {
        public static void Play(
            IEnumerable<FrameworkElement> elements,
            int staggerMilliseconds = 70,
            int durationMilliseconds = 320,
            double slideFromY = 18)
        {
            ArgumentNullException.ThrowIfNull(elements);

            int index = 0;
            foreach (FrameworkElement element in elements)
            {
                if (element.Visibility == Visibility.Collapsed)
                    continue;

                Prepare(element, slideFromY);
                Animate(element, TimeSpan.FromMilliseconds(index * staggerMilliseconds), durationMilliseconds, slideFromY);
                index++;
            }
        }

        private static void Prepare(FrameworkElement element, double slideFromY)
        {
            element.Opacity = 0;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new TranslateTransform(0, slideFromY);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            if (element.RenderTransform is TranslateTransform existing)
                existing.BeginAnimation(TranslateTransform.YProperty, null);
        }

        private static void Animate(
            FrameworkElement element,
            TimeSpan delay,
            int durationMilliseconds,
            double slideFromY)
        {
            if (element.RenderTransform is not TranslateTransform translate)
            {
                translate = new TranslateTransform(0, slideFromY);
                element.RenderTransform = translate;
            }

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMilliseconds))
            {
                BeginTime = delay,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd,
            };
            var slide = new DoubleAnimation(slideFromY, 0, TimeSpan.FromMilliseconds(durationMilliseconds + 40))
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd,
            };

            element.BeginAnimation(UIElement.OpacityProperty, fade);
            translate.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }
}
