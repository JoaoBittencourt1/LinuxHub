using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Lê idioma, layout de teclado e fuso da configuração do Windows em que o app está
    /// rodando. É a fronteira com a plataforma: toda a conversão para os formatos do Linux
    /// vive nos mapeadores puros (<see cref="LinuxLocaleMapper"/>,
    /// <see cref="WindowsKeyboardLayoutMapper"/>, <see cref="WindowsTimezoneMapper"/>), que
    /// são testáveis sem Windows nenhum.
    /// </summary>
    public sealed class SystemInfoProvider : ISystemInfoProvider
    {
        /// <summary>KL_NAMELENGTH: oito dígitos hex mais o terminador.</summary>
        private const int KeyboardLayoutIdLength = 9;

        /// <summary>LOCALE_NAME_MAX_LENGTH.</summary>
        private const int LocaleNameMaxLength = 85;

        /// <summary>
        /// Vem do Win32, e não de <see cref="CultureInfo.CurrentCulture"/>, porque o
        /// <c>LocalizationManager</c> reescreve a cultura do processo quando o usuário troca o
        /// idioma da INTERFACE do app — e o idioma da interface não é o idioma que o sistema
        /// instalado deve ter. Ler direto do Windows tira essa ambiguidade.
        /// </summary>
        public DetectedRegionalSetting GetLocale()
        {
            string locale = LinuxLocaleMapper.FromCulture(ReadUserDefaultCulture());

            return locale == LinuxLocaleMapper.FallbackLocale
                ? DetectedRegionalSetting.Fallback(locale)
                : DetectedRegionalSetting.Detected(locale);
        }

        public DetectedRegionalSetting GetKeymap() =>
            WindowsKeyboardLayoutMapper.ToLinuxKeymap(ReadActiveKeyboardLayoutId());

        public DetectedRegionalSetting GetTimezone() =>
            WindowsTimezoneMapper.ToIanaTimezone(TimeZoneInfo.Local.Id);

        private static CultureInfo? ReadUserDefaultCulture()
        {
            var buffer = new StringBuilder(LocaleNameMaxLength);
            if (GetUserDefaultLocaleName(buffer, buffer.Capacity) == 0)
                return null;

            try
            {
                return CultureInfo.GetCultureInfo(buffer.ToString());
            }
            catch (CultureNotFoundException)
            {
                // Locale personalizado do usuário, sem cultura correspondente no .NET. Não é
                // erro a esconder: é o caso "sem correspondência" da detecção, que cai no
                // padrão declarado e aparece marcado como tal no wizard.
                return null;
            }
        }

        /// <summary>
        /// O KLID é do layout ativo da THREAD que chama — daí este provider ser consultado a
        /// partir da thread de UI, ao montar o wizard. Numa thread sem entrada associada o
        /// Windows devolve o layout padrão do sistema, que continua sendo uma resposta
        /// razoável e revisável, nunca um valor inventado aqui.
        /// </summary>
        private static string? ReadActiveKeyboardLayoutId()
        {
            var buffer = new StringBuilder(KeyboardLayoutIdLength);
            return GetKeyboardLayoutName(buffer) ? buffer.ToString() : null;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetKeyboardLayoutName(StringBuilder pwszKlid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetUserDefaultLocaleName(StringBuilder lpLocaleName, int cchLocaleName);
    }
}
