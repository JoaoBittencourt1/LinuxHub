using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// <paramref name="IsoPath"/> é o caminho da ISO DENTRO do volume que a hospeda, não um
    /// caminho do Windows: o GRUB não conhece letra de unidade e localiza o arquivo com
    /// <c>search --file</c>, que compara caminhos relativos à raiz de cada volume. No modo
    /// substituir a ISO mora na partição de staging (<c>/linuxhub.iso</c>); no dual-boot
    /// continua no volume do Windows (ex.: <c>/Users/.../ubuntu.iso</c>), porque esse volume
    /// já é preservado pelo curtin.
    ///
    /// <paramref name="IsoHostPartitionUuid"/> só precisa vir preenchido no modo substituir,
    /// onde a ISO mora na partição de staging e <paramref name="IsoPath"/> já não é um caminho
    /// do Windows que se possa consultar. No dual-boot fica <c>null</c> e o serviço resolve a
    /// partir do caminho da ISO — e só quando a receita de boot em uso declara precisar dele.
    ///
    /// <paramref name="Unattended"/> é o que a instalação desatendida acrescenta à entrada de
    /// boot, já resolvido pelo preparer do mecanismo em uso — só faz sentido depois que a
    /// configuração correspondente foi gravada, porque sem ela presente o instalador para
    /// pedindo os dados que os parâmetros prometeram que existiriam. <c>null</c> (ou
    /// <see cref="UnattendedBootParameters.Interactive"/>) prepara o boot até o instalador
    /// nativo interativo.
    /// </summary>
    public sealed record BootStagingRequest(
        string DistroName,
        string IsoPath,
        bool IsUefi,
        int TargetDiskIndex,
        UnattendedBootParameters? Unattended = null,
        LiveSessionFamily LiveSession = LiveSessionFamily.Casper,
        string? IsoHostPartitionUuid = null);

    /// <summary>
    /// Instala o bootloader de staging (GRUB2 chainloaded) que permite bootar a ISO já
    /// baixada via loopback, sem USB — cobre UEFI (ESP + BCD) e BIOS legado (MBR, com
    /// backup do MBR original antes de qualquer escrita). Ver design.md D4 e specs
    /// boot-staging. Feature própria (não estende BootConfigurationService/
    /// DiskPartitioningService) por SRP — cada service concreto que ele orquestra continua
    /// com uma única responsabilidade.
    /// </summary>
    public interface IBootStagingService
    {
        void InstallStagingBootloader(BootStagingRequest request);
    }
}
