namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary><paramref name="EnableAutoinstall"/> liga o parâmetro <c>autoinstall</c> na
    /// linha de comando do kernel — só faz sentido quando a semente do cloud-init já foi
    /// gravada (<see cref="ICloudInitSeedWriter"/>), porque sem o <c>user-data</c> presente o
    /// instalador para pedindo os dados que o parâmetro prometeu que existiriam.</summary>
    public sealed record BootStagingRequest(
        string DistroName,
        string IsoPath,
        bool IsUefi,
        int TargetDiskIndex,
        bool EnableAutoinstall = false);

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
