using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Velocidade já vem suavizada/amostrada pelo service (ver IsoDownloadService) —
    /// quem consome não deve recalcular a partir de totais acumulados desde o início,
    /// isso é o que fazia a estimativa de tempo restante variar de forma errática.
    /// TotalBytes é null quando o servidor não informa Content-Length; nesse caso não
    /// há como calcular percentual nem ETA, e a UI deve tratar como progresso indeterminado.
    /// </summary>
    public readonly record struct IsoDownloadProgress(long BytesReceived, long? TotalBytes, double BytesPerSecond)
    {
        public double? PercentComplete => TotalBytes is > 0
            ? (double)BytesReceived / TotalBytes.Value * 100
            : null;
    }

    public interface IIsoDownloadService
    {
        /// <summary>
        /// Baixa a ISO da distro para a pasta padrão de ISOs do LinuxHub e retorna o
        /// caminho do arquivo baixado. Em caso de cancelamento, o arquivo parcial é
        /// removido e a exceção de cancelamento propaga para quem chamou. O SHA-256 é
        /// calculado em streaming durante o download e conferido contra
        /// <see cref="DistroInfo.Sha256"/>/<see cref="DistroInfo.SizeBytes"/>; uma
        /// divergência apaga o arquivo e lança <see cref="ArtifactVerificationException"/> —
        /// nenhum caminho de ISO é devolvido para um artefato que falhou a verificação.
        /// </summary>
        Task<string> DownloadAsync(DistroInfo distro, IProgress<IsoDownloadProgress> progress, CancellationToken cancellationToken);
    }
}
