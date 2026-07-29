using System.IO;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>Único lugar que sabe onde as ISOs baixadas pelo app ficam no disco —
    /// download e listagem precisam concordar sobre essa pasta.</summary>
    public static class IsoStorage
    {
        public static string BaseDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LinuxHub", "ISOs");
    }
}
