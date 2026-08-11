using System.Collections.Generic;
using System.Globalization;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Casa o país do usuário com uma região de mirror do Arch, para a instalação baixar
    /// pacotes de perto em vez de sortear um servidor do outro lado do mundo.
    ///
    /// O país sai do locale que o usuário já revisou no wizard (<c>pt_BR.UTF-8</c> → <c>BR</c>),
    /// e não de uma segunda leitura do Windows: é o mesmo valor que ele viu na tela, e uma
    /// leitura independente poderia divergir dele.
    ///
    /// Quando não há correspondência, nenhuma região é declarada — e isso é a resposta certa,
    /// não uma falha: sem <c>mirror_config</c> o archinstall usa a mirrorlist que a própria ISO
    /// traz, que é a lista global ordenada. Chutar um país vizinho seria pior que não escolher.
    /// </summary>
    public static class ArchMirrorRegions
    {
        /// <summary>
        /// As regiões que o Arch de fato publica, lidas de
        /// <c>archlinux.org/mirrors/status/json/</c> em 2026-08-11. A lista existe para que um
        /// nome de país do .NET que não seja uma região real do Arch simplesmente não case, em
        /// vez de virar uma região inválida na configuração — o nome é casado por igualdade
        /// exata do outro lado.
        /// </summary>
        private static readonly HashSet<string> PublishedRegions = new(StringComparer.Ordinal)
        {
            "Albania", "Argentina", "Armenia", "Australia", "Austria", "Azerbaijan", "Bangladesh",
            "Belarus", "Belgium", "Brazil", "Bulgaria", "Cambodia", "Canada", "Chile", "China",
            "Colombia", "Croatia", "Czechia", "Denmark", "Ecuador", "Estonia", "Finland", "France",
            "Georgia", "Germany", "Greece", "Hong Kong", "Hungary", "Iceland", "India", "Indonesia",
            "Iran", "Israel", "Italy", "Japan", "Kazakhstan", "Kenya", "Latvia", "Lithuania",
            "Luxembourg", "Malaysia", "Mauritius", "Mexico", "Moldova", "Morocco", "Nepal",
            "Netherlands", "New Caledonia", "New Zealand", "North Macedonia", "Norway", "Paraguay",
            "Philippines", "Poland", "Portugal", "Romania", "Russia", "Réunion", "Saudi Arabia",
            "Serbia", "Singapore", "Slovakia", "Slovenia", "South Africa", "South Korea", "Spain",
            "Sweden", "Switzerland", "Taiwan", "Thailand", "Tunisia", "Türkiye", "Ukraine",
            "United Arab Emirates", "United Kingdom", "United States", "Uzbekistan", "Vietnam",
        };

        /// <summary>
        /// A região de mirror correspondente ao locale, ou <c>null</c> quando o país não tem
        /// mirror publicado (ou o locale não tem país).
        /// </summary>
        public static string? FromLocale(string? locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                return null;

            // "pt_BR.UTF-8" → "BR". O que vem antes do sublinhado é o idioma, e o que interessa
            // aqui é o território.
            int underscore = locale.IndexOf('_');
            if (underscore < 0 || underscore + 3 > locale.Length)
                return null;

            string countryCode = locale.Substring(underscore + 1, 2);

            try
            {
                string country = new RegionInfo(countryCode).EnglishName;
                return PublishedRegions.Contains(country) ? country : null;
            }
            catch (ArgumentException)
            {
                // Código de país que o .NET não conhece. Como qualquer "sem correspondência"
                // aqui, o resultado é não declarar região — nunca inventar uma.
                return null;
            }
        }
    }
}
