using System.IO;
using System.Security.Cryptography;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed class ArtifactVerifier : IArtifactVerifier
    {
        private const int BufferSize = 1024 * 1024;

        public async Task<ArtifactVerificationResult> VerifyFileAsync(
            string filePath,
            string expectedSha256,
            long expectedSizeBytes,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

            var file = new FileInfo(filePath);
            if (!file.Exists)
                return ArtifactVerificationResult.FileMissing();

            // Tamanho é conferido antes do hash, e sem abrir o arquivo pra leitura: um artefato
            // de alguns GB com tamanho já divergente não vale o custo de uma leitura completa.
            if (file.Length != expectedSizeBytes)
                return ArtifactVerificationResult.SizeMismatch();

            string actualSha256 = await ComputeSha256Async(filePath, file.Length, progress, cancellationToken);

            return ArtifactVerificationClassifier.Classify(file.Length, expectedSizeBytes, actualSha256, expectedSha256);
        }

        private static async Task<string> ComputeSha256Async(
            string filePath, long totalBytes, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = new byte[BufferSize];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hasher.AppendData(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (totalBytes > 0)
                    progress?.Report((double)totalRead / totalBytes * 100);
            }

            return Convert.ToHexString(hasher.GetHashAndReset());
        }
    }
}
