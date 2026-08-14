using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LinuxHub.Common.Animations
{
    public enum EntranceEffect
    {
        None,
        Fade,
        FadeUp,
        FadeScale,
        FadeFromLeft,
        FadeFromRight,
    }

    /// <summary>
    /// Animação de entrada declarada no XAML: <c>anim:Entrance.Effect="FadeUp"</c>.
    /// Dispara em <c>IsVisibleChanged</c> (e não em <c>Loaded</c>) porque a navegação
    /// deste app é troca de <c>Visibility</c>/<c>Content</c> — um elemento já carregado
    /// que volta à tela precisa animar de novo, e <c>Loaded</c> só ocorreria uma vez.
    /// </summary>
    public static class Entrance
    {
        private static readonly Duration EffectDuration = new(TimeSpan.FromMilliseconds(420));
        private const double SlideOffset = 28d;
        private const double StartScale = 0.92d;

        /// <summary>Teto do escalonamento: sem ele, uma lista longa faria o último item
        /// esperar segundos para aparecer.</summary>
        private const int MaxStaggeredIndex = 14;

        public static readonly DependencyProperty EffectProperty = DependencyProperty.RegisterAttached(
            "Effect",
            typeof(EntranceEffect),
            typeof(Entrance),
            new PropertyMetadata(EntranceEffect.None, OnEffectChanged));

        public static EntranceEffect GetEffect(DependencyObject target) =>
            (EntranceEffect)target.GetValue(EffectProperty);

        public static void SetEffect(DependencyObject target, EntranceEffect value) =>
            target.SetValue(EffectProperty, value);

        /// <summary>Atraso fixo, em milissegundos, antes da animação começar.</summary>
        public static readonly DependencyProperty DelayProperty = DependencyProperty.RegisterAttached(
            "Delay", typeof(double), typeof(Entrance), new PropertyMetadata(0d));

        public static double GetDelay(DependencyObject target) => (double)target.GetValue(DelayProperty);

        public static void SetDelay(DependencyObject target, double value) => target.SetValue(DelayProperty, value);

        /// <summary>Atraso por posição entre os irmãos, em milissegundos. Serve para itens
        /// gerados por <c>ItemsControl</c>, onde o índice só existe em runtime.</summary>
        public static readonly DependencyProperty StaggerProperty = DependencyProperty.RegisterAttached(
            "Stagger", typeof(double), typeof(Entrance), new PropertyMetadata(0d));

        public static double GetStagger(DependencyObject target) => (double)target.GetValue(StaggerProperty);

        public static void SetStagger(DependencyObject target, double value) => target.SetValue(StaggerProperty, value);

        /// <summary>Aplica o efeito aos filhos de um painel, em cascata. Evita repetir os
        /// mesmos atributos em cada filho declarado no XAML.</summary>
        public static readonly DependencyProperty ChildEffectProperty = DependencyProperty.RegisterAttached(
            "ChildEffect",
            typeof(EntranceEffect),
            typeof(Entrance),
            new PropertyMetadata(EntranceEffect.None, OnChildEffectChanged));

        public static EntranceEffect GetChildEffect(DependencyObject target) =>
            (EntranceEffect)target.GetValue(ChildEffectProperty);

        public static void SetChildEffect(DependencyObject target, EntranceEffect value) =>
            target.SetValue(ChildEffectProperty, value);

        public static readonly DependencyProperty ChildStaggerProperty = DependencyProperty.RegisterAttached(
            "ChildStagger", typeof(double), typeof(Entrance), new PropertyMetadata(50d));

        public static double GetChildStagger(DependencyObject target) => (double)target.GetValue(ChildStaggerProperty);

        public static void SetChildStagger(DependencyObject target, double value) =>
            target.SetValue(ChildStaggerProperty, value);

        /// <summary>Instante da última execução, para descartar a segunda de um par. Um
        /// elemento pode ter efeito próprio E estar sob um painel com <c>ChildEffect</c>:
        /// ao ficar visível, os dois disparam. O painel corre primeiro (é quem tem o
        /// atraso do escalonamento), então vence — e o disparo próprio, redundante,
        /// é ignorado.</summary>
        private static readonly DependencyProperty LastPlayProperty = DependencyProperty.RegisterAttached(
            "LastPlay", typeof(long), typeof(Entrance), new PropertyMetadata(0L));

        private const long ReplayGuardMilliseconds = 100L;

        public static void Play(FrameworkElement element, EntranceEffect effect, TimeSpan delay)
        {
            if (effect == EntranceEffect.None)
                return;

            long now = Environment.TickCount64;
            if (now - (long)element.GetValue(LastPlayProperty) < ReplayGuardMilliseconds)
                return;

            element.SetValue(LastPlayProperty, now);

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // A opacidade final é lida ANTES de limpar a animação anterior: elementos
            // translúcidos de propósito (legendas com Opacity="0.6") precisam voltar ao
            // próprio valor, não a 1 — e depois da primeira execução o valor local já é 0.
            double finalOpacity = element.Opacity > 0.01d ? element.Opacity : 1d;

            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 0d;
            element.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0d, finalOpacity, EffectDuration) { BeginTime = delay, EasingFunction = ease });

            if (effect == EntranceEffect.Fade)
                return;

            var (scale, translate) = AnimatedTransform.Ensure(element);

            switch (effect)
            {
                case EntranceEffect.FadeUp:
                    Slide(translate, TranslateTransform.YProperty, SlideOffset, delay, ease);
                    break;
                case EntranceEffect.FadeFromLeft:
                    Slide(translate, TranslateTransform.XProperty, -SlideOffset, delay, ease);
                    break;
                case EntranceEffect.FadeFromRight:
                    Slide(translate, TranslateTransform.XProperty, SlideOffset, delay, ease);
                    break;
                case EntranceEffect.FadeScale:
                    Slide(scale, ScaleTransform.ScaleXProperty, StartScale, delay, ease, to: 1d);
                    Slide(scale, ScaleTransform.ScaleYProperty, StartScale, delay, ease, to: 1d);
                    break;
            }
        }

        private static void Slide(
            Transform target,
            DependencyProperty property,
            double from,
            TimeSpan delay,
            IEasingFunction ease,
            double to = 0d)
        {
            // O valor inicial é escrito na propriedade antes de animar: com BeginTime a
            // animação só assume o controle depois do atraso, e sem isso o elemento
            // apareceria na posição final e só então saltaria para trás.
            target.BeginAnimation(property, null);
            target.SetValue(property, from);
            target.BeginAnimation(
                property,
                new DoubleAnimation(from, to, EffectDuration) { BeginTime = delay, EasingFunction = ease });
        }

        private static void OnEffectChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            if (target is not FrameworkElement element)
                return;

            element.IsVisibleChanged -= OnElementVisibleChanged;

            if ((EntranceEffect)e.NewValue != EntranceEffect.None)
                element.IsVisibleChanged += OnElementVisibleChanged;
        }

        private static void OnElementVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue || sender is not FrameworkElement element)
                return;

            double delay = GetDelay(element) + GetStagger(element) * SiblingIndex(element);
            Play(element, GetEffect(element), TimeSpan.FromMilliseconds(delay));
        }

        private static void OnChildEffectChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            if (target is not Panel panel)
                return;

            panel.IsVisibleChanged -= OnPanelVisibleChanged;

            if ((EntranceEffect)e.NewValue != EntranceEffect.None)
                panel.IsVisibleChanged += OnPanelVisibleChanged;
        }

        private static void OnPanelVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue || sender is not Panel panel)
                return;

            var effect = GetChildEffect(panel);
            double stagger = GetChildStagger(panel);

            for (int index = 0; index < panel.Children.Count; index++)
            {
                if (panel.Children[index] is FrameworkElement child)
                    Play(child, effect, TimeSpan.FromMilliseconds(stagger * Math.Min(index, MaxStaggeredIndex)));
            }
        }

        /// <summary>Posição do elemento entre os irmãos do painel que o contém. Sobe a
        /// árvore visual porque um item de <c>ItemsControl</c> fica dentro de um
        /// <c>ContentPresenter</c> gerado — é ele, e não o item, que é filho do painel.</summary>
        private static int SiblingIndex(FrameworkElement element)
        {
            DependencyObject current = element;
            DependencyObject? parent = VisualTreeHelper.GetParent(current);

            while (parent is not null and not Panel)
            {
                current = parent;
                parent = VisualTreeHelper.GetParent(current);
            }

            if (parent is not Panel panel || current is not UIElement child)
                return 0;

            return Math.Min(Math.Max(panel.Children.IndexOf(child), 0), MaxStaggeredIndex);
        }
    }
}
