namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Função pura que classifica um tamanho/hash já calculados contra o esperado. Existe
    /// separada de <see cref="IArtifactVerifier"/> porque duas rotas diferentes chegam a um
    /// tamanho+hash já computados sem ler o arquivo do mesmo jeito: o verificador de arquivo lê
    /// e calcula numa passada só, e o download calcula em streaming durante a escrita (D10) —
    /// nenhum dos dois deveria reimplementar a comparação. Não é service (D18): não tem I/O nem
    /// ponto de variação, então não ganha interface.
    /// </summary>
    internal static class ArtifactVerificationClassifier
    {
        public static ArtifactVerificationResult Classify(
            long actualSizeBytes, long expectedSizeBytes, string actualSha256, string expectedSha256)
        {
            if (actualSizeBytes != expectedSizeBytes)
                return ArtifactVerificationResult.SizeMismatch();

            return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase)
                ? ArtifactVerificationResult.Verified()
                : ArtifactVerificationResult.HashMismatch();
        }
    }
}
