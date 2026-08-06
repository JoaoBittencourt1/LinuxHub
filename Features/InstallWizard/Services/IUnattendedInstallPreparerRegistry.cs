using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IUnattendedInstallPreparerRegistry
    {
        /// <summary>
        /// Lança quando o mecanismo é <see cref="UnattendedInstallMechanism.None"/> ou quando
        /// não há implementação registrada para ele — declarar automação que ninguém sabe
        /// gerar tem que estourar aqui, e não virar um boot que cai calado no instalador
        /// interativo depois do reboot.
        /// </summary>
        IUnattendedInstallPreparer Resolve(UnattendedInstallMechanism mechanism);
    }
}
