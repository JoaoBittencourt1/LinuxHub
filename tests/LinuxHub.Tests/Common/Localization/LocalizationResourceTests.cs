using System.Collections;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.Json;
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
        /// own-linux-installer task 10.6 (D14): a tela de progresso do instalador live não lê
        /// .resx — os textos viajam em <c>strings.linux.json</c>, dentro da mídia live. Esse
        /// arquivo não é uma segunda tradução mantida à mão (§3): este teste garante que toda
        /// chave nele existe nos dois <c>.resx</c> com o MESMO texto, então uma mudança de
        /// wording num lado sem o outro quebra o CI em vez de divergir silenciosamente.
        /// </summary>
        [Fact]
        public void LiveInstallerStrings_MatchTheResxTheyAreSourcedFrom()
        {
            string repoRoot = FindRepoRoot();
            string linuxStringsPath = Path.Combine(
                repoRoot, "live-media", "rootfs-overlay", "opt", "linuxhub", "catalog", "strings.linux.json");
            Assert.True(File.Exists(linuxStringsPath), $"Arquivo esperado não existe: {linuxStringsPath}");

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(linuxStringsPath));

            var cultureByLocaleTag = new Dictionary<string, CultureInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["pt-BR"] = CultureInfo.InvariantCulture, // Strings.resx (padrão) é pt-BR
                ["en-US"] = new CultureInfo("en-US"),
            };

            foreach (JsonProperty entry in document.RootElement.EnumerateObject())
            {
                if (entry.Name.StartsWith('_'))
                    continue; // "_comment" etc — metadado do arquivo, não uma chave de texto.

                foreach ((string localeTag, CultureInfo culture) in cultureByLocaleTag)
                {
                    Assert.True(
                        entry.Value.TryGetProperty(localeTag, out JsonElement translationElement),
                        $"strings.linux.json: chave '{entry.Name}' não tem tradução '{localeTag}'");

                    string linuxText = translationElement.GetString() ?? string.Empty;
                    string? resxText = Resources.GetString(entry.Name, culture);

                    Assert.False(
                        string.IsNullOrWhiteSpace(resxText),
                        $"chave '{entry.Name}' citada em strings.linux.json não existe em Strings{(culture.Equals(CultureInfo.InvariantCulture) ? "" : "." + culture.Name)}.resx");
                    Assert.Equal(resxText, linuxText);
                }
            }
        }

        private static string FindRepoRoot()
        {
            string? cursor = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && cursor is not null; i++)
            {
                if (File.Exists(Path.Combine(cursor, "LinuxHub.csproj")))
                    return cursor;

                cursor = Directory.GetParent(cursor)?.FullName;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root (LinuxHub.csproj) from " + AppContext.BaseDirectory);
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
