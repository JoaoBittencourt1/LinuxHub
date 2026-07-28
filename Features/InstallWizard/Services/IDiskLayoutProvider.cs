using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IDiskLayoutProvider
    {
        /// <summary>
        /// Lê o layout real do disco informado. Deve ser chamado DEPOIS do shrink — é o
        /// espaço livre resultante que define onde a partição Linux vai nascer.
        /// </summary>
        DiskLayout GetLayout(int diskIndex);
    }
}
