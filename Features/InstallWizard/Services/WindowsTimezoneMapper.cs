using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Converte o identificador de fuso do Windows (<c>"E. South America Standard Time"</c>)
    /// para o identificador IANA que o Linux usa (<c>"America/Sao_Paulo"</c>).
    ///
    /// O mapeamento inteiro vem da BCL (<see cref="TimeZoneInfo.TryConvertWindowsIdToIanaId"/>,
    /// .NET 6+), que lê o CLDR do ICU — manter uma tabela à mão aqui seria reescrever um dado
    /// que já existe no runtime, e que muda quando a tzdata muda. Classe pura: recebe o id
    /// como texto, não vai ao sistema (ver <see cref="SystemInfoProvider"/>).
    /// </summary>
    public static class WindowsTimezoneMapper
    {
        /// <summary>Padrão declarado para fuso sem correspondência conhecida. UTC é o único
        /// valor que não erra "menos" para uns e "mais" para outros — e chega ao wizard
        /// marcado como padrão, para o usuário corrigir.</summary>
        public const string FallbackTimezone = "UTC";

        public static DetectedRegionalSetting ToIanaTimezone(string? windowsTimeZoneId)
        {
            if (string.IsNullOrWhiteSpace(windowsTimeZoneId))
                return DetectedRegionalSetting.Fallback(FallbackTimezone);

            return TimeZoneInfo.TryConvertWindowsIdToIanaId(windowsTimeZoneId, out string? ianaId)
                ? DetectedRegionalSetting.Detected(ianaId)
                : DetectedRegionalSetting.Fallback(FallbackTimezone);
        }

        /// <summary>
        /// Os fusos IANA oferecidos ao usuário para revisão. Sai da mesma conversão usada na
        /// detecção, aplicada a todos os fusos que o sistema conhece: uma segunda lista,
        /// montada por outro caminho, poderia não conter o valor detectado — e um item
        /// ausente da lista aparece como campo em branco no seletor.
        /// </summary>
        public static IReadOnlyList<string> KnownIanaTimezones() =>
            TimeZoneInfo.GetSystemTimeZones()
                .Select(zone => ToIanaTimezone(zone.Id).Value)
                .Append(FallbackTimezone)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
    }
}
