using System.Windows;
using System.Windows.Media;

namespace LinuxHub.Common.Animations
{
    /// <summary>
    /// Os efeitos deste namespace (entrada, hover, clique) mexem em escala e
    /// deslocamento do mesmo elemento. Se cada um criasse seu próprio
    /// <see cref="Transform"/>, o último a escrever em <c>RenderTransform</c>
    /// apagaria o do outro — por isso existe um único <see cref="TransformGroup"/>
    /// por elemento, criado aqui e reaproveitado por todos.
    /// </summary>
    internal static class AnimatedTransform
    {
        private const int ScaleIndex = 0;
        private const int TranslateIndex = 1;

        public static (ScaleTransform Scale, TranslateTransform Translate) Ensure(FrameworkElement element)
        {
            if (element.RenderTransform is TransformGroup group
                && group.Children.Count == 2
                && group.Children[ScaleIndex] is ScaleTransform existingScale
                && group.Children[TranslateIndex] is TranslateTransform existingTranslate)
            {
                return (existingScale, existingTranslate);
            }

            var scale = new ScaleTransform(1d, 1d);
            var translate = new TranslateTransform(0d, 0d);

            // Sem origem no centro, escalar empurra o elemento para a direita/baixo
            // em vez de crescer no lugar.
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new TransformGroup { Children = { scale, translate } };

            return (scale, translate);
        }
    }
}
