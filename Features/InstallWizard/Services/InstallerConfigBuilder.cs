using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed record BuildInstallerConfigRequest(
        DistroInfo Distro,
        string IsoPath,
        bool IsUefi,
        InstallMode Mode,
        int? TargetDiskIndex,
        int? TargetPartitionIndex,
        int LinuxPartitionSizeGb,
        string Username,
        string Password,
        string Hostname,
        string Locale,
        string Keymap,
        string Timezone,
        string DesktopEnvironment = "");

    /// <summary>
    /// Monta um <see cref="InstallerConfig"/> a partir do estado do wizard. Não depende
    /// de System.Windows.* — testável isoladamente, ao contrário do BuildInstallerConfig
    /// original que lia direto de controles do MainWindow.
    /// </summary>
    public sealed class InstallerConfigBuilder
    {
        private readonly IEspLocatorService _espLocator;

        public InstallerConfigBuilder(IEspLocatorService espLocator)
        {
            _espLocator = espLocator ?? throw new ArgumentNullException(nameof(espLocator));
        }

        public InstallerConfig Build(BuildInstallerConfigRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var cfg = new InstallerConfig
            {
                DistroId = request.Distro.Id,
                DistroName = request.Distro.Name,
                DistroFamily = request.Distro.Family,
                DistroVersion = request.Distro.Version,
                IsoPath = request.IsoPath,

                BootMode = request.IsUefi ? "uefi" : "bios",
                InstallMode = request.Mode == InstallMode.Replace ? "replace" : "dualboot",
                EfiPartitionIndex = request.IsUefi && request.TargetDiskIndex.HasValue
                    ? _espLocator.FindEfiSystemPartitionIndex(request.TargetDiskIndex.Value)
                    : null,
                TargetDiskIndex = request.TargetDiskIndex ?? 0,

                Username = request.Username.Trim(),
                Password = request.Password,
                Hostname = request.Hostname.Trim(),

                // Vêm do pedido, não de uma leitura do sistema feita aqui: o passo regional do
                // wizard mostra esses três valores e permite corrigi-los, e ler de novo neste
                // ponto reabriria a divergência entre o que o usuário viu e o que foi gravado.
                Locale = request.Locale,
                Timezone = request.Timezone,
                Keymap = request.Keymap,
                DesktopEnvironment = request.DesktopEnvironment,

                SwapEnabled = true,
                SwapSizeGb = 8
            };

            if (request.Mode == InstallMode.DualBoot && request.TargetPartitionIndex.HasValue)
            {
                cfg.TargetPartitionIndex = request.TargetPartitionIndex;
                cfg.LinuxPartitionSizeGb = request.LinuxPartitionSizeGb;
            }
            else
            {
                cfg.TargetPartitionIndex = null;
                cfg.LinuxPartitionSizeGb = 0;
            }

            return cfg;
        }
    }
}
