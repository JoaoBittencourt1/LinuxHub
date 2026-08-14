using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// <paramref name="SeedPartitionNumber"/> é a partição de semente criada no disco alvo,
    /// que a limpeza pós-instalação precisa remover.
    /// </summary>
    public sealed record UnattendedPreparationResult(
        int SeedPartitionNumber,
        UnattendedBootParameters BootParameters);

    /// <summary>
    /// Deixa o disco pronto para uma instalação desatendida no formato do instalador nativo
    /// daquela distro, e devolve o que a entrada de boot precisa acrescentar para ativá-la.
    ///
    /// Uma implementação por <see cref="UnattendedInstallMechanism"/>: os formatos não têm
    /// denominador comum útil (YAML curtin contra debconf/partman), então o que se compartilha
    /// é a entrada — <see cref="InstallerConfig"/> e o layout do disco —, não a saída.
    /// </summary>
    public interface IUnattendedInstallPreparer
    {
        /// <summary>O mecanismo que esta implementação atende. Nunca
        /// <see cref="UnattendedInstallMechanism.None"/>.</summary>
        UnattendedInstallMechanism Mechanism { get; }

        /// <summary>
        /// No modo substituir, <paramref name="staging"/> é obrigatória e deve existir ANTES
        /// desta chamada — a configuração gerada a declara como preservada; sem isso o
        /// instalador a trata como espaço disponível e apaga a ISO que está usando para rodar.
        /// No dual-boot a ISO fica no volume do Windows e <paramref name="staging"/> é
        /// <c>null</c>.
        /// </summary>
        UnattendedPreparationResult Prepare(
            InstallerConfig config, int diskIndex, StagingPartition? staging);
    }
}
