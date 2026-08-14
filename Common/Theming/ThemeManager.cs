using System.ComponentModel;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace LinuxHub.Common.Theming
{
    /// <summary>
    /// Fonte única do tema da aplicação. Existe em vez de chamar
    /// <see cref="ApplicationThemeManager"/> direto de cada lugar porque aquele é
    /// estático e não notifica mudanças — sem isto, nenhum elemento de UI conseguiria
    /// reagir à troca de tema por binding.
    /// </summary>
    public sealed class ThemeManager : INotifyPropertyChanged
    {
        public static ThemeManager Instance { get; } = new();

        private ThemeManager()
        {
        }

        public ApplicationTheme Current { get; private set; } = ApplicationTheme.Dark;

        public bool IsDark => Current == ApplicationTheme.Dark;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Apply(ApplicationTheme theme)
        {
            Current = theme;

            // O backdrop vai junto do tema: sem repassá-lo, a janela mantém o material
            // Mica calculado para o tema anterior e a barra de título fica destoando
            // do resto da janela até o próximo redimensionamento.
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDark)));
        }

        public void Toggle() => Apply(IsDark ? ApplicationTheme.Light : ApplicationTheme.Dark);
    }
}
