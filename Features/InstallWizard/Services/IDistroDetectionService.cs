using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary><paramref name="IsExpectedVersion"/> só é relevante quando
    /// <see cref="DistroInfo.SupportsUnattendedInstall"/> é true — um arquivo cujo nome não bate com
    /// a versão testada do catálogo (ex.: "ubuntu-26.04...iso" contra o Ubuntu 24.04 validado)
    /// não impede a seleção nem desliga o toggle de autoinstall, só liga um alerta de que ele
    /// pode não funcionar nessa versão.</summary>
    public sealed record DistroDetectionResult(DistroInfo Distro, bool IsExpectedVersion);

    public interface IDistroDetectionService
    {
        /// <summary>
        /// Identifica a distro a partir do nome do arquivo ISO. Nunca retorna distro null —
        /// usa uma distro "desconhecida" como fallback, para não travar o fluxo.
        /// </summary>
        DistroDetectionResult Detect(string isoPath);
    }
}
