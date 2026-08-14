using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace LinuxHub.Common.Animations
{
    public enum HoverEffect
    {
        None,

        /// <summary>Crescimento discreto — para botões comuns.</summary>
        Pop,

        /// <summary>Crescimento com elevação — para cartões clicáveis.</summary>
        Lift,

        /// <summary>Só o brilho, sem transformar o elemento — para botões esticados,
        /// onde escalar deslocaria visivelmente as bordas.</summary>
        Glow,
    }

    /// <summary>
    /// Reação ao ponteiro declarada no XAML: <c>anim:Hover.Effect="Lift"</c>.
    /// É código, e não um <c>Style</c> com <c>Trigger</c>, porque os controles vêm do
    /// WPF-UI: um estilo implícito sobre eles teria que herdar do estilo padrão da
    /// biblioteca e, quando o tema é trocado em runtime, essa herança é refeita —
    /// um behavior anexado sobrevive à troca de tema sem precisar saber dela.
    /// </summary>
    public static class Hover
    {
        private static readonly Duration EnterDuration = new(TimeSpan.FromMilliseconds(170));
        private static readonly Duration LeaveDuration = new(TimeSpan.FromMilliseconds(260));
        private static readonly Duration PressDuration = new(TimeSpan.FromMilliseconds(90));

        public static readonly DependencyProperty EffectProperty = DependencyProperty.RegisterAttached(
            "Effect",
            typeof(HoverEffect),
            typeof(Hover),
            new PropertyMetadata(HoverEffect.None, OnEffectChanged));

        public static HoverEffect GetEffect(DependencyObject target) => (HoverEffect)target.GetValue(EffectProperty);

        public static void SetEffect(DependencyObject target, HoverEffect value) => target.SetValue(EffectProperty, value);

        /// <summary>Cor do brilho: um hexadecimal (ex.: <c>#E95420</c>) ou o nome de um
        /// recurso <see cref="Color"/> do tema (ex.: <c>SystemFillColorSuccess</c>). Vazio
        /// usa a cor de destaque do tema.
        ///
        /// A regra de qual usar: o que representa uma distro brilha na cor da marca dela —
        /// o cartão do Ubuntu em laranja, o do Manjaro em verde. Botão brilha na cor do
        /// próprio significado: o de instalar é verde, o de confirmar destruição é
        /// vermelho. Um botão verde soltando brilho laranja lê como alerta.</summary>
        public static readonly DependencyProperty GlowColorProperty = DependencyProperty.RegisterAttached(
            "GlowColor", typeof(string), typeof(Hover), new PropertyMetadata(string.Empty));

        public static string GetGlowColor(DependencyObject target) => (string)target.GetValue(GlowColorProperty);

        public static void SetGlowColor(DependencyObject target, string value) => target.SetValue(GlowColorProperty, value);

        private sealed record HoverProfile(
            double Scale,
            double PressScale,
            double Lift,
            double GlowRadius,
            double GlowOpacity);

        private static HoverProfile ProfileFor(HoverEffect effect) => effect switch
        {
            HoverEffect.Pop => new HoverProfile(
                Scale: 1.04d, PressScale: 0.97d, Lift: -2d, GlowRadius: 18d, GlowOpacity: 0.45d),

            HoverEffect.Lift => new HoverProfile(
                Scale: 1.06d, PressScale: 0.98d, Lift: -6d, GlowRadius: 30d, GlowOpacity: 0.60d),

            // O afundamento no clique é menor aqui pelo mesmo motivo que não há
            // crescimento no hover: o alvo típico ocupa a largura toda, e qualquer
            // escala perceptível descola as bordas dele das do vizinho.
            HoverEffect.Glow => new HoverProfile(
                Scale: 1d, PressScale: 0.99d, Lift: 0d, GlowRadius: 26d, GlowOpacity: 0.55d),

            _ => new HoverProfile(
                Scale: 1d, PressScale: 1d, Lift: 0d, GlowRadius: 0d, GlowOpacity: 0d),
        };

        private static void OnEffectChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            if (target is not FrameworkElement element)
                return;

            element.MouseEnter -= OnMouseEnter;
            element.MouseLeave -= OnMouseLeave;
            element.PreviewMouseLeftButtonDown -= OnMouseDown;
            element.PreviewMouseLeftButtonUp -= OnMouseUp;

            if ((HoverEffect)e.NewValue == HoverEffect.None)
                return;

            element.MouseEnter += OnMouseEnter;
            element.MouseLeave += OnMouseLeave;
            element.PreviewMouseLeftButtonDown += OnMouseDown;
            element.PreviewMouseLeftButtonUp += OnMouseUp;
        }

        private static void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            var profile = ProfileFor(GetEffect(element));
            AnimateTransform(element, profile.Scale, profile.Lift, EnterDuration);
            ShowGlow(element, profile);
        }

        private static void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            AnimateTransform(element, scale: 1d, lift: 0d, LeaveDuration);
            HideGlow(element);
        }

        private static void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            var profile = ProfileFor(GetEffect(element));
            AnimateTransform(element, profile.PressScale, lift: 0d, PressDuration);
        }

        private static void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            var profile = ProfileFor(GetEffect(element));

            if (element.IsMouseOver)
                AnimateTransform(element, profile.Scale, profile.Lift, EnterDuration);
            else
                AnimateTransform(element, scale: 1d, lift: 0d, LeaveDuration);
        }

        private static void AnimateTransform(FrameworkElement element, double scale, double lift, Duration duration)
        {
            var (scaleTransform, translateTransform) = AnimatedTransform.Ensure(element);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var scaleAnimation = new DoubleAnimation(scale, duration) { EasingFunction = ease };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);

            translateTransform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(lift, duration) { EasingFunction = ease });
        }

        /// <summary>
        /// O brilho só existe enquanto o ponteiro está sobre o elemento: um
        /// <see cref="DropShadowEffect"/> permanente tira todo o subárvore do caminho
        /// acelerado de renderização, e a grade do catálogo tem uma dezena de cartões.
        /// </summary>
        private static void ShowGlow(FrameworkElement element, HoverProfile profile)
        {
            if (profile.GlowRadius <= 0d)
                return;

            if (element.Effect is not DropShadowEffect glow)
            {
                glow = new DropShadowEffect
                {
                    ShadowDepth = 0d,
                    BlurRadius = 0d,
                    Opacity = 0d,
                };
                element.Effect = glow;
            }

            // A cor é reatribuída a cada entrada, e não só na criação: o mesmo elemento
            // pode representar outra distro depois (o cartão de detalhe é reaproveitado),
            // e um brilho laranja num Manjaro seria pior que brilho nenhum.
            glow.Color = ResolveGlowColor(element);

            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(profile.GlowRadius, EnterDuration));
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(profile.GlowOpacity, EnterDuration));
        }

        private static void HideGlow(FrameworkElement element)
        {
            if (element.Effect is not DropShadowEffect glow)
                return;

            var fadeOut = new DoubleAnimation(0d, LeaveDuration);
            fadeOut.Completed += (_, _) =>
            {
                if (!element.IsMouseOver && ReferenceEquals(element.Effect, glow))
                    element.Effect = null;
            };

            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(0d, LeaveDuration));
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, fadeOut);
        }

        /// <summary>Cor da marca quando o elemento declara uma; senão, a cor de destaque
        /// do tema atual — um brilho preto sumiria no tema escuro e um branco, no claro.</summary>
        private static Color ResolveGlowColor(FrameworkElement element)
        {
            string configured = GetGlowColor(element);

            if (configured.StartsWith('#'))
            {
                try
                {
                    if (ColorConverter.ConvertFromString(configured) is Color brand)
                        return brand;
                }
                catch (FormatException)
                {
                    // Hexadecimal malformado no catálogo ou no XAML. Cair no destaque do
                    // tema é degradação correta: não há ação do usuário a tomar, e um
                    // brilho na cor errada é melhor que a tela toda falhar.
                }
            }
            else if (!string.IsNullOrWhiteSpace(configured)
                && Application.Current?.TryFindResource(configured) is Color themed)
            {
                // Cor vinda do tema, e não fixa no XAML: o verde de sucesso e o vermelho
                // de perigo do Fluent não são os mesmos no tema claro e no escuro.
                return themed;
            }

            return Application.Current?.TryFindResource("SystemAccentColorSecondary") is Color accent
                ? accent
                : Colors.Gray;
        }
    }
}
