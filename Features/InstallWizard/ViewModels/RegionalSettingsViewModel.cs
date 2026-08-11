using LinuxHub.Common.Mvvm;
using LinuxHub.Features.InstallWizard.Services;

namespace LinuxHub.Features.InstallWizard.ViewModels
{
    /// <summary>
    /// Idioma, layout de teclado e fuso do sistema a ser instalado: detectados do Windows e
    /// editáveis. Ver specs/install-wizard/spec.md — "Permitir revisar as informações
    /// regionais antes de instalar".
    ///
    /// Detectar sem deixar revisar repetiria o defeito antigo em outra forma: a detecção pode
    /// errar, e um teclado físico diferente do configurado no Windows é caso comum. Perguntar
    /// do zero cansaria quem já configurou o Windows do jeito certo. Daí os três campos
    /// nascerem preenchidos e mudáveis.
    /// </summary>
    public class RegionalSettingsViewModel : ObservableObject
    {
        private string _locale;
        private string _keymap;
        private string _timezone;

        public RegionalSettingsViewModel(ISystemInfoProvider systemInfo)
        {
            ArgumentNullException.ThrowIfNull(systemInfo);

            var locale = systemInfo.GetLocale();
            var keymap = systemInfo.GetKeymap();
            var timezone = systemInfo.GetTimezone();

            _locale = locale.Value;
            _keymap = keymap.Value;
            _timezone = timezone.Value;

            HasUndetectedSetting = locale.IsFallback || keymap.IsFallback || timezone.IsFallback;
        }

        /// <summary>As opções saem dos mesmos mapeadores que produzem os valores detectados —
        /// uma lista montada por outro caminho poderia não conter o detectado, e o seletor
        /// abriria em branco sobre um valor que mesmo assim seria gravado.</summary>
        public IReadOnlyList<LinuxLocaleOption> AvailableLocales { get; } = LinuxLocaleMapper.KnownLocales();

        public IReadOnlyList<string> AvailableKeymaps { get; } = WindowsKeyboardLayoutMapper.SupportedKeymaps;

        public IReadOnlyList<string> AvailableTimezones { get; } = WindowsTimezoneMapper.KnownIanaTimezones();

        public string Locale
        {
            get => _locale;
            set => SetProperty(ref _locale, value);
        }

        public string Keymap
        {
            get => _keymap;
            set => SetProperty(ref _keymap, value);
        }

        public string Timezone
        {
            get => _timezone;
            set => SetProperty(ref _timezone, value);
        }

        /// <summary>Ao menos um dos três não teve correspondência e está mostrando o padrão
        /// declarado do mapeamento. Não impede nada — só torna visível o que antes era um
        /// chute calado, para o usuário conferir esse campo antes de instalar.</summary>
        public bool HasUndetectedSetting { get; }
    }
}
