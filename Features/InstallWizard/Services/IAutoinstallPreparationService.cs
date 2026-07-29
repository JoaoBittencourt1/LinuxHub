using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IAutoinstallPreparationService
    {
        /// <summary>
        /// Deixa o disco pronto para uma instalação desatendida: cria a partição de semente,
        /// lê o layout resultante, gera o <c>user-data</c> a partir dele e grava. Devolve o
        /// número da partição de semente, que a limpeza pós-instalação precisa remover.
        ///
        /// No modo substituir, <paramref name="staging"/> é obrigatória e deve existir ANTES
        /// desta chamada — o storage config a declara como preservada; sem isso o curtin a
        /// trata como espaço disponível e apaga a ISO. No dual-boot a ISO fica no volume do
        /// Windows e <paramref name="staging"/> é <c>null</c>.
        /// </summary>
        int Prepare(InstallerConfig config, int diskIndex, StagingPartition? staging);
    }
}
