using System.Globalization;
using System.Text.RegularExpressions;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>Um locale oferecido ao usuário: o valor que vai para a instalação e o nome do
    /// idioma no próprio idioma — <c>"pt_BR.UTF-8"</c> sozinho é reconhecível, mas
    /// "português (Brasil)" ao lado dispensa decifrar o código.</summary>
    public sealed record LinuxLocaleOption(string Value, string NativeName);

    /// <summary>
    /// Converte uma cultura do .NET no locale que o Linux espera: <c>pt-BR</c> vira
    /// <c>pt_BR.UTF-8</c>. Classe pura — quem descobre a cultura do Windows é o
    /// <see cref="SystemInfoProvider"/>.
    /// </summary>
    public static class LinuxLocaleMapper
    {
        public const string FallbackLocale = "en_US.UTF-8";

        /// <summary>Só <c>idioma-REGIÃO</c> vira locale: o Linux nomeia locales como
        /// <c>língua_TERRITÓRIO</c>, e culturas neutras (<c>pt</c>) ou com script
        /// (<c>zh-Hans-CN</c>) não têm equivalente com esse nome.</summary>
        private static readonly Regex LanguageAndRegion =
            new("^[a-z]{2,3}-[A-Z]{2}$", RegexOptions.Compiled);

        public static string FromCulture(CultureInfo? culture)
        {
            if (culture is null || !LanguageAndRegion.IsMatch(culture.Name))
                return FallbackLocale;

            return culture.Name.Replace('-', '_') + ".UTF-8";
        }

        /// <summary>
        /// Os locales oferecidos para revisão, pela mesma conversão usada na detecção — uma
        /// lista montada por outro caminho poderia não conter o valor detectado, e um item
        /// ausente aparece como campo em branco no seletor.
        /// </summary>
        public static IReadOnlyList<LinuxLocaleOption> KnownLocales() =>
            CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Where(culture => LanguageAndRegion.IsMatch(culture.Name))
                .Select(culture => new LinuxLocaleOption(FromCulture(culture), culture.NativeName))
                .DistinctBy(option => option.Value, StringComparer.Ordinal)
                .OrderBy(option => option.NativeName, StringComparer.CurrentCulture)
                .ToArray();
    }
}
