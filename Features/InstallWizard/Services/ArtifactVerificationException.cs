namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>Um artefato baixado ou verificado não corresponde ao hash/tamanho esperado. Quem
    /// lança já removeu o arquivo — nenhum caminho de ISO fica disponível depois desta exceção
    /// (spec artifact-integrity: nenhum "prosseguir mesmo assim").</summary>
    public sealed class ArtifactVerificationException : Exception
    {
        public ArtifactVerificationOutcome Outcome { get; }

        public ArtifactVerificationException(ArtifactVerificationOutcome outcome, string message)
            : base(message)
        {
            Outcome = outcome;
        }
    }
}
