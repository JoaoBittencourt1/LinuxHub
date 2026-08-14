namespace LinuxHub.Features.InstallWizard.Services
{
    public enum ArtifactVerificationOutcome
    {
        Verified,
        SizeMismatch,
        HashMismatch,
        FileMissing,
    }

    public readonly record struct ArtifactVerificationResult(ArtifactVerificationOutcome Outcome)
    {
        public bool IsVerified => Outcome == ArtifactVerificationOutcome.Verified;

        public static ArtifactVerificationResult Verified() => new(ArtifactVerificationOutcome.Verified);
        public static ArtifactVerificationResult SizeMismatch() => new(ArtifactVerificationOutcome.SizeMismatch);
        public static ArtifactVerificationResult HashMismatch() => new(ArtifactVerificationOutcome.HashMismatch);
        public static ArtifactVerificationResult FileMissing() => new(ArtifactVerificationOutcome.FileMissing);
    }

    /// <summary>Verifica um artefato já presente em disco (ISO selecionada manualmente ou
    /// reaproveitada de um download anterior) contra o hash e o tamanho esperados. Não cobre o
    /// download em si — <see cref="IIsoDownloadService"/> calcula o hash em streaming durante a
    /// escrita, sem uma segunda passada pelo arquivo (ver design.md, D10).</summary>
    public interface IArtifactVerifier
    {
        Task<ArtifactVerificationResult> VerifyFileAsync(
            string filePath,
            string expectedSha256,
            long expectedSizeBytes,
            IProgress<double>? progress,
            CancellationToken cancellationToken);
    }
}
