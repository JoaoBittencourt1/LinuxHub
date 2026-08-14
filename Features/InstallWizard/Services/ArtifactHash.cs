using System.IO;
using System.Security.Cryptography;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Streaming SHA-256 for recording observed artifact identity when the catalog does not
    /// declare a hash (unverified local ISO). Not a substitute for catalog verification.
    /// </summary>
    public static class ArtifactHash
    {
        private const int BufferSize = 1024 * 1024;

        public static string ComputeSha256(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = new byte[BufferSize];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                hasher.AppendData(buffer, 0, bytesRead);

            return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }
    }
}
