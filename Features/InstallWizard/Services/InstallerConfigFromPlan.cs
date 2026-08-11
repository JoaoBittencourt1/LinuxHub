using System.IO;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Derives <see cref="InstallerConfig"/> from a published plan. Password is read from the
    /// sidecar path recorded in the plan — never reconstructed from UI state (task 3.9).
    /// </summary>
    public static class InstallerConfigFromPlan
    {
        public static InstallerConfig Derive(InstallationPlan plan, int? efiPartitionIndex = null)
        {
            ArgumentNullException.ThrowIfNull(plan);
            InstallationPlanValidator.Validate(plan);

            string password = File.ReadAllText(plan.Account.PasswordWindowsPath).TrimEnd('\n', '\r');

            var config = new InstallerConfig
            {
                DistroId = plan.Distribution.Id,
                DistroName = plan.Distribution.Name,
                DistroFamily = plan.Distribution.Family,
                DistroVersion = plan.Distribution.Version,
                IsoPath = plan.Distribution.IsoWindowsPath,

                BootMode = plan.Firmware,
                InstallMode = plan.InstallMode,
                TargetDiskIndex = plan.Disk.Number,
                EfiPartitionIndex = efiPartitionIndex,
                TargetPartitionIndex = string.Equals(
                        plan.InstallMode,
                        InstallationPlanInstallMode.DualBoot,
                        StringComparison.Ordinal)
                    ? plan.Disk.Windows.Number
                    : null,
                LinuxPartitionSizeGb = plan.Disk.Installer.FinalSizeGiB,

                Username = plan.Account.Username,
                Password = password,
                Hostname = plan.Account.Hostname,

                Locale = plan.Locale.Locale,
                Timezone = plan.Locale.Timezone,
                Keymap = plan.Locale.Keymap,
                DesktopEnvironment = plan.Locale.DesktopEnvironment ?? string.Empty,

                SwapEnabled = true,
                SwapSizeGb = 8,
            };

            EnsureConfigMatchesPlan(config, plan);
            return config;
        }

        /// <summary>
        /// Aborts before mutation when a derived config diverges from the published plan.
        /// </summary>
        public static void EnsureConfigMatchesPlan(InstallerConfig config, InstallationPlan plan)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(plan);

            var errors = new List<string>();

            if (!string.Equals(config.DistroId, plan.Distribution.Id, StringComparison.Ordinal))
                errors.Add("DistroId diverges from the published plan.");
            if (!string.Equals(config.BootMode, plan.Firmware, StringComparison.Ordinal))
                errors.Add("BootMode diverges from the published plan.");
            if (!string.Equals(config.InstallMode, plan.InstallMode, StringComparison.Ordinal))
                errors.Add("InstallMode diverges from the published plan.");
            if (config.TargetDiskIndex != plan.Disk.Number)
                errors.Add("TargetDiskIndex diverges from the published plan.");
            if (!string.Equals(
                    Path.GetFullPath(config.IsoPath),
                    Path.GetFullPath(plan.Distribution.IsoWindowsPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("IsoPath diverges from the published plan.");
            }
            if (!string.Equals(config.Username, plan.Account.Username, StringComparison.Ordinal))
                errors.Add("Username diverges from the published plan.");
            if (!string.Equals(config.Hostname, plan.Account.Hostname, StringComparison.Ordinal))
                errors.Add("Hostname diverges from the published plan.");

            if (errors.Count > 0)
                throw new InvalidOperationException(
                    "InstallerConfig diverges from the published plan: " + string.Join(" ", errors));
        }
    }
}
