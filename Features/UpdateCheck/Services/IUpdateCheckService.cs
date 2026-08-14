namespace LinuxHub.Features.UpdateCheck.Services
{
    /// <summary>Última release publicada do projeto: a versão e onde baixá-la.</summary>
    internal sealed record LatestRelease(Version Version, Uri Url);

    /// <summary>
    /// Consulta qual é a última release publicada do projeto. Livre de tipos de WPF
    /// (constitution §5): não decide exibir nada e não sabe que existe UI — devolve o
    /// resultado e deixa a falha propagar para quem orquestra.
    /// </summary>
    internal interface IUpdateCheckService
    {
        Task<LatestRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default);
    }
}
