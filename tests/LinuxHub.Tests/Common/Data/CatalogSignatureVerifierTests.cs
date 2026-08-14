using System.Security.Cryptography;
using System.Text;
using LinuxHub.Common.Data;
using Xunit;

namespace LinuxHub.Tests.Common.Data
{
    /// <summary>
    /// Cobre o gate criptográfico do catálogo remoto (D8): nenhum documento é aceito sem uma
    /// assinatura válida contra a chave pública embarcada. Usa um par RSA descartável gerado só
    /// para este arquivo de teste — nunca a chave de produção nem o placeholder de
    /// CatalogPublicKey, para que o teste continue valendo depois que a chave real for trocada.
    /// </summary>
    public class CatalogSignatureVerifierTests
    {
        private static (string PublicPem, RSA PrivateKey) GenerateTestKeypair()
        {
            var rsa = RSA.Create(2048);
            return (rsa.ExportSubjectPublicKeyInfoPem(), rsa);
        }

        private static byte[] Sign(RSA privateKey, byte[] documentBytes) =>
            privateKey.SignData(documentBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        [Fact]
        public void Verify_ValidSignature_ReturnsTrue()
        {
            var (publicPem, privateKey) = GenerateTestKeypair();
            using (privateKey)
            {
                byte[] document = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"distributions\":[]}");
                byte[] signature = Sign(privateKey, document);

                using var verifier = new CatalogSignatureVerifier(publicPem);

                Assert.True(verifier.Verify(document, signature));
            }
        }

        [Fact]
        public void Verify_DocumentTamperedAfterSigning_ReturnsFalse()
        {
            var (publicPem, privateKey) = GenerateTestKeypair();
            using (privateKey)
            {
                byte[] original = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"distributions\":[]}");
                byte[] signature = Sign(privateKey, original);
                byte[] tampered = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"distributions\":[{}]}");

                using var verifier = new CatalogSignatureVerifier(publicPem);

                Assert.False(verifier.Verify(tampered, signature));
            }
        }

        /// <summary>O caso que mais importa: um documento assinado por uma chave que não é a
        /// embarcada não pode passar. É o que separa "catálogo assinado" de "catálogo com um
        /// campo chamado assinatura".</summary>
        [Fact]
        public void Verify_SignedByADifferentKey_ReturnsFalse()
        {
            var (_, signingKey) = GenerateTestKeypair();
            var (verifierPublicPem, otherKey) = GenerateTestKeypair();
            using (signingKey)
            using (otherKey)
            {
                byte[] document = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"distributions\":[]}");
                byte[] signature = Sign(signingKey, document);

                using var verifier = new CatalogSignatureVerifier(verifierPublicPem);

                Assert.False(verifier.Verify(document, signature));
            }
        }

        [Fact]
        public void Verify_GarbageSignatureBytes_ReturnsFalseWithoutThrowing()
        {
            var (publicPem, privateKey) = GenerateTestKeypair();
            privateKey.Dispose();

            byte[] document = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"distributions\":[]}");
            using var verifier = new CatalogSignatureVerifier(publicPem);

            var result = verifier.Verify(document, new byte[] { 1, 2, 3 });

            Assert.False(result);
        }

        /// <summary>A chave embarcada de verdade (CatalogPublicKey, usada pelo construtor
        /// público) precisa ser um PEM válido — um erro de colar a chave (linha cortada,
        /// caractere a mais) só apareceria em runtime sem este teste.</summary>
        [Fact]
        public void Verify_DefaultConstructor_LoadsTheEmbeddedPublicKeyWithoutThrowing()
        {
            using var verifier = new CatalogSignatureVerifier();

            var result = verifier.Verify(new byte[] { 1 }, new byte[] { 2 });

            Assert.False(result);
        }
    }
}
