using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace LinuxHub.Common.Ui
{
    /// <summary>
    /// Soft breathing glow for primary CTAs (install). Visual-only attached behavior.
    /// </summary>
    public static class AttentionPulse
    {
        private static readonly DependencyProperty StoryboardProperty =
            DependencyProperty.RegisterAttached(
                "Storyboard",
                typeof(Storyboard),
                typeof(AttentionPulse));

        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached(
                "Enabled",
                typeof(bool),
                typeof(AttentionPulse),
                new PropertyMetadata(false, OnEnabledChanged));

        public static readonly DependencyProperty GlowColorProperty =
            DependencyProperty.RegisterAttached(
                "GlowColor",
                typeof(Color),
                typeof(AttentionPulse),
                new PropertyMetadata(Color.FromRgb(0x16, 0xC6, 0x0A)));

        public static bool GetEnabled(DependencyObject element) =>
            (bool)element.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject element, bool value) =>
            element.SetValue(EnabledProperty, value);

        public static Color GetGlowColor(DependencyObject element) =>
            (Color)element.GetValue(GlowColorProperty);

        public static void SetGlowColor(DependencyObject element, Color value) =>
            element.SetValue(GlowColorProperty, value);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            if ((bool)e.NewValue)
            {
                element.Loaded += StartPulse;
                element.Unloaded += StopPulse;
                if (element.IsLoaded)
                    StartPulse(element, new RoutedEventArgs());
            }
            else
            {
                element.Loaded -= StartPulse;
                element.Unloaded -= StopPulse;
                StopPulse(element, new RoutedEventArgs());
            }
        }

        private static void StartPulse(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            StopPulse(element, e);

            var glow = new DropShadowEffect
            {
                Color = GetGlowColor(element),
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.35,
            };
            element.Effect = glow;

            var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            var blur = new DoubleAnimation(10, 24, TimeSpan.FromMilliseconds(1100))
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            var opacity = new DoubleAnimation(0.3, 0.75, TimeSpan.FromMilliseconds(1100))
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };

            Storyboard.SetTarget(blur, glow);
            Storyboard.SetTargetProperty(blur, new PropertyPath(DropShadowEffect.BlurRadiusProperty));
            Storyboard.SetTarget(opacity, glow);
            Storyboard.SetTargetProperty(opacity, new PropertyPath(DropShadowEffect.OpacityProperty));
            storyboard.Children.Add(blur);
            storyboard.Children.Add(opacity);
            storyboard.Begin(element, isControllable: true);
            element.SetValue(StoryboardProperty, storyboard);
        }

        private static void StopPulse(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            if (element.GetValue(StoryboardProperty) is Storyboard storyboard)
            {
                storyboard.Stop(element);
                element.ClearValue(StoryboardProperty);
            }

            element.Effect = null;
        }
    }
}
