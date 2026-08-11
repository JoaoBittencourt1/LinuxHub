using System.IO;
using System.Security.Cryptography;
using System.Text;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Cobre o gate da mudança adopt-redacted-safety-model: nenhum artefato local é aceito só
    /// porque o nome bate — o conteúdo precisa corresponder ao hash e ao tamanho publicados pela
    /// fonte oficial (specs/artifact-integrity).
    /// </summary>
    public class ArtifactVerifierTests
    {
        private static readonly byte[] KnownContent = Encoding.UTF8.GetBytes("linuxhub-artifact-integrity-fixture");
        private static readonly string KnownSha256 = Convert.ToHexString(SHA256.HashData(KnownContent));

        private static string WriteTempFile(byte[] content)
        {
            string path = Path.Combine(Path.GetTempPath(), $"linuxhub-artifact-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(path, content);
            return path;
        }

        [Fact]
        public async Task VerifyFileAsync_MatchingHashAndSize_ReturnsVerified()
        {
            string path = WriteTempFile(KnownContent);
            try
            {
                var result = await new ArtifactVerifier().VerifyFileAsync(
                    path, KnownSha256, KnownContent.Length, progress: null, CancellationToken.None);

                Assert.True(result.IsVerified);
                Assert.Equal(ArtifactVerificationOutcome.Verified, result.Outcome);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Um arquivo do tamanho certo mas com bytes diferentes (ex.: download
        /// corrompido, ou adulterado) tem que ser pego mesmo quando o tamanho bate.</summary>
        [Fact]
        public async Task VerifyFileAsync_SameSizeDifferentContent_ReturnsHashMismatch()
        {
            byte[] tampered = Encoding.UTF8.GetBytes("linuxhub-artifact-integrity-FIXTURE");
            Assert.Equal(KnownContent.Length, tampered.Length);

            string path = WriteTempFile(tampered);
            try
            {
                var result = await new ArtifactVerifier().VerifyFileAsync(
                    path, KnownSha256, KnownContent.Length, progress: null, CancellationToken.None);

                Assert.False(result.IsVerified);
                Assert.Equal(ArtifactVerificationOutcome.HashMismatch, result.Outcome);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Tamanho divergente rejeita sem precisar ler o arquivo inteiro — o hash correto
        /// de um arquivo do tamanho errado nunca é sequer calculado.</summary>
        [Fact]
        public async Task VerifyFileAsync_SizeMismatch_ReturnsSizeMismatchWithoutComputingHash()
        {
            string path = WriteTempFile(KnownContent);
            try
            {
                var result = await new ArtifactVerifier().VerifyFileAsync(
                    path, KnownSha256, KnownContent.Length + 1, progress: null, CancellationToken.None);

                Assert.False(result.IsVerified);
                Assert.Equal(ArtifactVerificationOutcome.SizeMismatch, result.Outcome);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task VerifyFileAsync_MissingFile_ReturnsFileMissing()
        {
            string path = Path.Combine(Path.GetTempPath(), $"linuxhub-missing-{Guid.NewGuid():N}.iso");

            var result = await new ArtifactVerifier().VerifyFileAsync(
                path, KnownSha256, KnownContent.Length, progress: null, CancellationToken.None);

            Assert.Equal(ArtifactVerificationOutcome.FileMissing, result.Outcome);
        }

        /// <summary>O hash declarado no catálogo é sempre minúsculo; a comparação não pode
        /// depender de quem gerou o valor ter usado a mesma caixa.</summary>
        [Fact]
        public async Task VerifyFileAsync_HashComparisonIsCaseInsensitive()
        {
            string path = WriteTempFile(KnownContent);
            try
            {
                var result = await new ArtifactVerifier().VerifyFileAsync(
                    path, KnownSha256.ToLowerInvariant(), KnownContent.Length, progress: null, CancellationToken.None);

                Assert.True(result.IsVerified);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task VerifyFileAsync_ReportsProgress()
        {
            byte[] content = new byte[5 * 1024 * 1024];
            new Random(42).NextBytes(content);
            string expectedHash = Convert.ToHexString(SHA256.HashData(content));
            string path = WriteTempFile(content);

            try
            {
                var reports = new List<double>();
                var progress = new Progress<double>(reports.Add);

                await new ArtifactVerifier().VerifyFileAsync(
                    path, expectedHash, content.Length, progress, CancellationToken.None);

                // Progress<T> marshals via SynchronizationContext.Post; sem um contexto de UI
                // no teste, os callbacks já chegam síncronos — nenhuma espera adicional é
                // necessária, mas a lista não pode ficar vazia num arquivo de vários MB.
                Assert.NotEmpty(reports);
                Assert.Equal(100, reports[^1], precision: 0);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
