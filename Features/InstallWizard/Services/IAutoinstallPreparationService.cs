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
        /// Deve rodar DEPOIS do encolhimento e DEPOIS da partição de staging existir — é o
        /// espaço livre resultante que define onde a partição raiz do Linux vai nascer, e a
        /// staging precisa aparecer no layout lido para ser declarada como preservada. Sem
        /// isso o curtin a trata como espaço disponível e apaga a ISO que está usando.
        /// </summary>
        int Prepare(InstallerConfig config, int diskIndex, StagingPartition staging);
    }
}
