using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace LinuxHub.Common.Ui
{
    /// <summary>
    /// Short fade/slide when swapping shell content.
    /// </summary>
    public static class ContentTransition
    {
        public static void PlayEnter(FrameworkElement content)
        {
            ArgumentNullException.ThrowIfNull(content);

            content.Opacity = 0;
            content.RenderTransformOrigin = new Point(0.5, 0.5);
            var translate = new System.Windows.Media.TranslateTransform(0, 12);
            content.RenderTransform = translate;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };

            content.BeginAnimation(UIElement.OpacityProperty, fade);
            translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
        }

        public static void PlayEnter(ContentControl host)
        {
            ArgumentNullException.ThrowIfNull(host);
            if (host.Content is FrameworkElement content)
                PlayEnter(content);
        }
    }
}
