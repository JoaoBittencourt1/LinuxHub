using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using LinuxHub.Common.Data;
using LinuxHub.Common.Models;
using Xunit;

namespace LinuxHub.Tests.Common.Data
{
    /// <summary>
    /// Orquestra fetch + verificação + merge. A rede é substituída por um
    /// <see cref="HttpMessageHandler"/> fake (CatalogClient aceita um <c>Func&lt;HttpClient&gt;</c>
    /// via construtor internal só para isto) — sem isso, a parte que mais precisa de teste
    /// (assinatura inválida descarta o documento inteiro; rede indisponível cai pro resultado
    /// certo) ficaria sem cobertura nenhuma, como acontece hoje com IsoDownloadService.
    /// </summary>
    public class CatalogClientTests
    {
        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            public byte[]? DocumentBytes { get; set; }
            public string? SignatureBase64 { get; set; }
            public bool ThrowOnRequest { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (ThrowOnRequest)
                    throw new HttpRequestException("simulated network failure");

                bool isSignatureRequest = request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = isSignatureRequest
                        ? new StringContent(SignatureBase64 ?? string.Empty)
                        : new ByteArrayContent(DocumentBytes ?? Array.Empty<byte>()),
                };
                return Task.FromResult(response);
            }
        }

        private static readonly DistroInfo LocalUbuntu = new()
        {
            Id = "ubuntu",
            Name = "Ubuntu",
            ImagePath = "pack://application:,,,/Assets/Images/Ubuntu.png",
        };

        private static (string PublicPem, RSA PrivateKey) GenerateTestKeypair()
        {
            var rsa = RSA.Create(2048);
            return (rsa.ExportSubjectPublicKeyInfoPem(), rsa);
        }

        private const string ValidDocumentJson = """
            {
              "schemaVersion": 1,
              "distributions": [
                {
                  "id": "ubuntu",
                  "name": "Ubuntu (remoto)",
                  "family": "Debian",
                  "version": "24.04.5",
                  "createdYear": "2004",
                  "beginnerRating": 5,
                  "isTested": true,
                  "isEnabled": true,
                  "unattendedInstall": "Subiquity",
                  "liveSession": "Casper",
                  "downloadLink": "https://ubuntu.com/download/desktop",
                  "directDownloadLink": "https://releases.ubuntu.com/24.04/ubuntu-24.04.5-desktop-amd64.iso",
                  "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "sizeBytes": 123
                }
              ]
            }
            """;

        private static CatalogClient BuildClient(FakeHttpMessageHandler handler, string publicPem) =>
            new(new CatalogSourceConfig("https://example.com/catalog"),
                new CatalogSignatureVerifier(publicPem),
                new[] { LocalUbuntu },
                () => new HttpClient(handler));

        [Fact]
        public async Task FetchAsync_ValidSignatureAndDocument_ReturnsVerifiedWithMergedDistros()
        {
            var (publicPem, privateKey) = GenerateTestKeypair();
            using (privateKey)
            {
                byte[] documentBytes = Encoding.UTF8.GetBytes(ValidDocumentJson);
                byte[] signature = privateKey.SignData(documentBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                var handler = new FakeHttpMessageHandler
                {
                    DocumentBytes = documentBytes,
                    SignatureBase64 = Convert.ToBase64String(signature),
                };
                var client = BuildClient(handler, publicPem);

                var result = await client.FetchAsync(CancellationToken.None);

                Assert.True(result.IsVerified);
                var ubuntu = Assert.Single(result.Distros!);
                Assert.Equal("Ubuntu (remoto)", ubuntu.Name);
                Assert.Equal(LocalUbuntu.ImagePath, ubuntu.ImagePath);
            }
        }

        [Fact]
        public async Task FetchAsync_SignatureFromAnotherKey_ReturnsSignatureInvalidAndNoDistros()
        {
            var (_, signingKey) = GenerateTestKeypair();
            var (verifierPublicPem, otherKey) = GenerateTestKeypair();
            using (signingKey)
            using (otherKey)
            {
                byte[] documentBytes = Encoding.UTF8.GetBytes(ValidDocumentJson);
                byte[] signature = signingKey.SignData(documentBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                var handler = new FakeHttpMessageHandler
                {
                    DocumentBytes = documentBytes,
                    SignatureBase64 = Convert.ToBase64String(signature),
                };
                var client = BuildClient(handler, verifierPublicPem);

                var result = await client.FetchAsync(CancellationToken.None);

                Assert.Equal(CatalogFetchOutcome.SignatureInvalid, result.Outcome);
                Assert.Null(result.Distros);
            }
        }

        [Fact]
        public async Task FetchAsync_MalformedJsonDespiteValidSignature_ReturnsMalformedDocument()
        {
            var (publicPem, privateKey) = GenerateTestKeypair();
            using (privateKey)
            {
                byte[] documentBytes = Encoding.UTF8.GetBytes("{ not valid json");
                byte[] signature = privateKey.SignData(documentBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                var handler = new FakeHttpMessageHandler
                {
                    DocumentBytes = documentBytes,
                    SignatureBase64 = Convert.ToBase64String(signature),
                };
                var client = BuildClient(handler, publicPem);

                var result = await client.FetchAsync(CancellationToken.None);

                Assert.Equal(CatalogFetchOutcome.MalformedDocument, result.Outcome);
            }
        }

        [Fact]
        public async Task FetchAsync_UnsupportedSchemaVersion_ReturnsMalformedDocument()
        {
            var (publicPem, privateKey) = GenerateTestKeypair();
            using (privateKey)
            {
                byte[] documentBytes = Encoding.UTF8.GetBytes("""{"schemaVersion":2,"distributions":[]}""");
                byte[] signature = privateKey.SignData(documentBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                var handler = new FakeHttpMessageHandler
                {
                    DocumentBytes = documentBytes,
                    SignatureBase64 = Convert.ToBase64String(signature),
                };
                var client = BuildClient(handler, publicPem);

                var result = await client.FetchAsync(CancellationToken.None);

                Assert.Equal(CatalogFetchOutcome.MalformedDocument, result.Outcome);
            }
        }

        [Fact]
        public async Task FetchAsync_NetworkFailure_ReturnsNetworkUnavailable()
        {
            var (publicPem, _) = GenerateTestKeypair();
            var handler = new FakeHttpMessageHandler { ThrowOnRequest = true };
            var client = BuildClient(handler, publicPem);

            var result = await client.FetchAsync(CancellationToken.None);

            Assert.Equal(CatalogFetchOutcome.NetworkUnavailable, result.Outcome);
            Assert.Null(result.Distros);
        }
    }
}
