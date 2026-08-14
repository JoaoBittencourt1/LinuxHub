using System.Globalization;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class LinuxLocaleMapperTests
    {
        [Theory]
        [InlineData("pt-BR", "pt_BR.UTF-8")]
        [InlineData("en-US", "en_US.UTF-8")]
        [InlineData("de-DE", "de_DE.UTF-8")]
        public void SpecificCulture_BecomesTheLinuxLocale(string cultureName, string expected) =>
            Assert.Equal(expected, LinuxLocaleMapper.FromCulture(CultureInfo.GetCultureInfo(cultureName)));

        /// <summary>Locale do Linux é <c>língua_TERRITÓRIO</c>. Uma cultura neutra viraria
        /// <c>pt.UTF-8</c> e a invariante viraria <c>.UTF-8</c> — nomes que não existem em
        /// nenhum sistema, e que só falhariam lá na instalação.</summary>
        [Theory]
        [InlineData("pt")]
        [InlineData("")]
        public void CultureWithoutRegion_FallsBackToTheDeclaredDefault(string cultureName) =>
            Assert.Equal(
                LinuxLocaleMapper.FallbackLocale,
                LinuxLocaleMapper.FromCulture(CultureInfo.GetCultureInfo(cultureName)));

        [Fact]
        public void NullCulture_FallsBackToTheDeclaredDefault() =>
            Assert.Equal(LinuxLocaleMapper.FallbackLocale, LinuxLocaleMapper.FromCulture(null));

        /// <summary>A lista do seletor precisa conter o que a detecção produz — inclusive o
        /// padrão declarado, que é o valor exibido quando nada foi reconhecido.</summary>
        [Fact]
        public void KnownLocales_ContainTheFallbackAndAreUnique()
        {
            var locales = LinuxLocaleMapper.KnownLocales();

            Assert.Contains(locales, option => option.Value == LinuxLocaleMapper.FallbackLocale);
            Assert.Contains(locales, option => option.Value == "pt_BR.UTF-8");
            Assert.Equal(locales.Select(option => option.Value).Distinct().Count(), locales.Count);
        }
    }
}
