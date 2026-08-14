using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>Função pura compartilhada pelo verificador de arquivo e pelo download em
    /// streaming (D10) — os dois precisam classificar tamanho/hash já calculados do mesmo jeito.</summary>
    public class ArtifactVerificationClassifierTests
    {
        private const string Hash = "aa11bb22cc33dd44ee55ff66aa11bb22cc33dd44ee55ff66aa11bb22cc33dd44";

        [Fact]
        public void Classify_MatchingSizeAndHash_ReturnsVerified()
        {
            var result = ArtifactVerificationClassifier.Classify(100, 100, Hash, Hash);

            Assert.True(result.IsVerified);
        }

        [Fact]
        public void Classify_SizeMismatch_ReturnsSizeMismatchRegardlessOfHash()
        {
            var result = ArtifactVerificationClassifier.Classify(99, 100, Hash, Hash);

            Assert.Equal(ArtifactVerificationOutcome.SizeMismatch, result.Outcome);
        }

        [Fact]
        public void Classify_SizeMatchesHashDiffers_ReturnsHashMismatch()
        {
            var result = ArtifactVerificationClassifier.Classify(100, 100, Hash, Hash.Replace('a', 'b'));

            Assert.Equal(ArtifactVerificationOutcome.HashMismatch, result.Outcome);
        }

        [Fact]
        public void Classify_HashComparisonIsCaseInsensitive()
        {
            var result = ArtifactVerificationClassifier.Classify(100, 100, Hash.ToUpperInvariant(), Hash);

            Assert.True(result.IsVerified);
        }
    }
}
