using System.IO;
using LinuxHub.Common.Data;
using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed class DownloadedIsoRepository : IDownloadedIsoRepository
    {
        public IReadOnlyList<DownloadedIso> GetAll()
        {
            if (!Directory.Exists(IsoStorage.BaseDirectory))
                return Array.Empty<DownloadedIso>();

            return Directory.EnumerateFiles(IsoStorage.BaseDirectory, "*.iso")
                .Select(path =>
                {
                    string id = Path.GetFileNameWithoutExtension(path);
                    var distro = DistroCatalog.All.FirstOrDefault(d => d.Id == id);
                    var downloadedAtUtc = File.GetLastWriteTimeUtc(path);
                    return new DownloadedIso(path, distro, downloadedAtUtc);
                })
                // Um arquivo cujo nome não bate com nenhum Id do catálogo (ex.: catálogo mudou,
                // ou alguém largou um .iso qualquer na pasta) não tem template de autoinstall/
                // GRUB associado — não faz sentido oferecer como opção de instalação.
                .Where(iso => iso.Distro is not null)
                .OrderByDescending(iso => iso.DownloadedAtUtc)
                .ToList();
        }
    }
}
