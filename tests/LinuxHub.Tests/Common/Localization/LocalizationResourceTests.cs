using System.Collections;
using System.Globalization;
using System.Resources;
using LinuxHub.Common.Localization;
using Xunit;

namespace LinuxHub.Tests.Common.Localization
{
    /// <summary>
    /// A constitution (§4) proíbe string de UI hardcoded: todo texto vive nos <c>.resx</c>.
    /// O risco que sobra é uma chave existir em um idioma e não no outro — e esse defeito é
    /// silencioso, porque o indexador do <see cref="LocalizationManager"/> devolve a PRÓPRIA
    /// chave quando não acha o recurso. O sintoma na tela é "Shell_PreviewWarningTitle" no
    /// lugar do aviso, e nada quebra no build.
    /// </summary>
    public class LocalizationResourceTests
    {
        private static readonly ResourceManager Resources =
            new("LinuxHub.Common.Localization.Strings", typeof(LocalizationManager).Assembly);

        [Theory]
        [InlineData("Shell_PreviewWarningTitle")]
        [InlineData("Shell_PreviewWarningMessage")]
        [InlineData("Wizard_TestedDistroWarningTitle")]
        [InlineData("Wizard_TestedDistroWarningMessage")]
        public void RiskWarnings_HaveTextInEveryLanguage(string key)
        {
            // Estes são avisos de risco: o usuário está prestes a deixar um app em
            // desenvolvimento reparticionar o disco dele. Aparecer em branco, ou aparecer como
            // o nome da chave, é o mesmo que não avisar.
            foreach (LanguageOption language in LocalizationManager.AvailableLanguages)
            {
                string? text = Resources.GetString(key, language.Culture);

                Assert.False(
                    string.IsNullOrWhiteSpace(text),
                    $"a chave '{key}' não tem texto em {language.Culture.Name}");
                Assert.NotEqual(key, text);
            }
        }

        [Fact]
        public void EveryTranslatedLanguage_CoversEveryKeyOfTheDefaultLanguage()
        {
            HashSet<string> defaultKeys = KeysDeclaredIn(CultureInfo.InvariantCulture);
            Assert.NotEmpty(defaultKeys);

            // Um dos idiomas é o padrão (Strings.resx, sem sufixo) e por isso não tem
            // ResourceSet próprio; os demais são satélites e precisam cobrir tudo.
            var translated = LocalizationManager.AvailableLanguages
                .Select(language => (language.Culture, Keys: KeysDeclaredIn(language.Culture)))
                .Where(entry => entry.Keys.Count > 0)
                .ToList();

            Assert.NotEmpty(translated);

            foreach ((CultureInfo culture, HashSet<string> keys) in translated)
            {
                string[] missing = defaultKeys.Except(keys).Order(StringComparer.Ordinal).ToArray();

                Assert.True(
                    missing.Length == 0,
                    $"{culture.Name} não traduz: {string.Join(", ", missing)}");
            }
        }

        /// <summary>
        /// Chaves declaradas para a cultura exata, SEM o fallback para o idioma padrão — que é
        /// o ponto: com fallback, uma chave faltando no satélite devolve o texto do padrão e o
        /// teste passaria sem detectar nada.
        /// </summary>
        private static HashSet<string> KeysDeclaredIn(CultureInfo culture)
        {
            ResourceSet? set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);

            return set is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : set.Cast<DictionaryEntry>()
                    .Select(entry => (string)entry.Key)
                    .ToHashSet(StringComparer.Ordinal);
        }
    }
}
