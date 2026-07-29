using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IDownloadedIsoRepository
    {
        /// <summary>ISOs já baixadas, da mais recente para a mais antiga.</summary>
        IReadOnlyList<DownloadedIso> GetAll();
    }
}
