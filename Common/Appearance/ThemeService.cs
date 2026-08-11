using System.IO;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace LinuxHub.Common.Appearance
{
    public interface IThemeService
    {
        ApplicationTheme Current { get; }

        bool IsDark { get; }

        void ApplyPersistedOrDefault();

        void Apply(ApplicationTheme theme);

        void Toggle();
    }

    /// <summary>
    /// App-wide Fluent theme via WPF-UI. Default is Light; preference persists under
    /// LocalAppData so the choice survives restarts.
    /// </summary>
    public sealed class ThemeService : IThemeService
    {
        private readonly string _settingsPath;

        public ThemeService()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LinuxHub",
                "ui-theme.txt"))
        {
        }

        internal ThemeService(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
                throw new ArgumentException("A theme settings path is required.", nameof(settingsPath));

            _settingsPath = settingsPath;
            string? dir = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        public ApplicationTheme Current { get; private set; } = ApplicationTheme.Light;

        public bool IsDark => Current == ApplicationTheme.Dark;

        public void ApplyPersistedOrDefault()
        {
            Apply(ReadPersisted() ?? ApplicationTheme.Light);
        }

        public void Apply(ApplicationTheme theme)
        {
            Current = theme;
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
            Persist(theme);
        }

        public void Toggle() =>
            Apply(IsDark ? ApplicationTheme.Light : ApplicationTheme.Dark);

        private ApplicationTheme? ReadPersisted()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return null;

                string value = File.ReadAllText(_settingsPath).Trim();
                if (string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase))
                    return ApplicationTheme.Dark;
                if (string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase))
                    return ApplicationTheme.Light;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Preferência é opcional — cai no Light. UnauthorizedAccessException entra
                // aqui porque isto roda em App.OnStartup, ANTES de existir qualquer janela:
                // escapar mataria o app sem nenhuma tela para reportar o erro, e por causa
                // de uma escolha de tema.
            }

            return null;
        }

        private void Persist(ApplicationTheme theme)
        {
            try
            {
                File.WriteAllText(
                    _settingsPath,
                    theme == ApplicationTheme.Dark ? "Dark" : "Light");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Não fatal: o tema vale para esta sessão mesmo sem conseguir gravar.
            }
        }
    }
}
