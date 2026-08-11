using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LinuxHub.Common.Ui
{
    /// <summary>
    /// Scale-bounce feedback on press. WPF visual-only — attached from XAML (constitution
    /// §1 allows code-behind/visual effects that cannot live in the ViewModel).
    /// </summary>
    public static class PressFeedback
    {
        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached(
                "Enabled",
                typeof(bool),
                typeof(PressFeedback),
                new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject element) =>
            (bool)element.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject element, bool value) =>
            element.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            if ((bool)e.NewValue)
            {
                EnsureTransform(element);
                element.PreviewMouseLeftButtonDown += OnPressed;
            }
            else
            {
                element.PreviewMouseLeftButtonDown -= OnPressed;
            }
        }

        private static void EnsureTransform(FrameworkElement element)
        {
            if (element.RenderTransform is ScaleTransform)
                return;

            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new ScaleTransform(1, 1);
        }

        private static void OnPressed(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            EnsureTransform(element);
            if (element.RenderTransform is not ScaleTransform scale)
                return;

            var press = new DoubleAnimation(1.0, 0.92, TimeSpan.FromMilliseconds(70))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            var release = new DoubleAnimation(0.92, 1.06, TimeSpan.FromMilliseconds(120))
            {
                BeginTime = TimeSpan.FromMilliseconds(70),
                EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut },
            };
            var settle = new DoubleAnimation(1.06, 1.0, TimeSpan.FromMilliseconds(140))
            {
                BeginTime = TimeSpan.FromMilliseconds(190),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            };

            var storyboard = new Storyboard();
            AddScale(storyboard, press, scale, ScaleTransform.ScaleXProperty);
            AddScale(storyboard, press.Clone(), scale, ScaleTransform.ScaleYProperty);
            AddScale(storyboard, release, scale, ScaleTransform.ScaleXProperty);
            AddScale(storyboard, release.Clone(), scale, ScaleTransform.ScaleYProperty);
            AddScale(storyboard, settle, scale, ScaleTransform.ScaleXProperty);
            AddScale(storyboard, settle.Clone(), scale, ScaleTransform.ScaleYProperty);
            storyboard.Begin();
        }

        private static void AddScale(
            Storyboard storyboard,
            Timeline animation,
            DependencyObject target,
            DependencyProperty property)
        {
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            storyboard.Children.Add(animation);
        }
    }
}
