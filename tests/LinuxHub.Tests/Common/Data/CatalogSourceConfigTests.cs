using LinuxHub.Common.Data;
using Xunit;

namespace LinuxHub.Tests.Common.Data
{
    public class CatalogSourceConfigTests
    {
        [Fact]
        public void NoOverride_ResolvesToThePlaceholderUrl()
        {
            var config = new CatalogSourceConfig(overrideBaseUrl: null);

            Assert.Equal($"{CatalogSourceConfig.PlaceholderBaseUrl}/catalog.json", config.CatalogDocumentUrl.ToString());
            Assert.Equal($"{CatalogSourceConfig.PlaceholderBaseUrl}/catalog.json.sig", config.CatalogSignatureUrl.ToString());
        }

        [Theory]
        [InlineData("https://example.com/catalog", "https://example.com/catalog/catalog.json")]
        [InlineData("https://example.com/catalog/", "https://example.com/catalog/catalog.json")]
        public void ValidOverride_BuildsDocumentAndSignatureUrls(string overrideBaseUrl, string expectedDocumentUrl)
        {
            var config = new CatalogSourceConfig(overrideBaseUrl);

            Assert.Equal(expectedDocumentUrl, config.CatalogDocumentUrl.ToString());
            Assert.Equal(expectedDocumentUrl + ".sig", config.CatalogSignatureUrl.ToString());
        }

        /// <summary>HTTP puro (sem TLS) transportaria um documento e uma assinatura que ainda
        /// seriam verificados criptograficamente — mas aceitar o override sem TLS normaliza um
        /// hábito operacional ruim por nenhum ganho real, já que HTTPS é suportado por toda
        /// hospedagem estática minimamente séria.</summary>
        [Fact]
        public void NonHttpsOverride_Throws() =>
            Assert.Throws<ArgumentException>(() => new CatalogSourceConfig("http://example.com/catalog"));

        [Fact]
        public void MalformedOverride_Throws() =>
            Assert.Throws<ArgumentException>(() => new CatalogSourceConfig("not-a-url"));

        [Fact]
        public void RelativeOverride_Throws() =>
            Assert.Throws<ArgumentException>(() => new CatalogSourceConfig("/catalog"));
    }
}
