using System.IO;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed record BuildInstallationPlanRequest(
        DistroInfo Distro,
        string IsoWindowsPath,
        long IsoSizeBytes,
        string IsoSha256,
        string IsoUrl,
        bool IsUefi,
        InstallMode Mode,
        DiskLayout Layout,
        int LogicalSectorSizeBytes,
        string SystemDrive,
        string DiskUniqueId,
        string PartitionTableId,
        InstallationPlanPartitionIdentity WindowsPartition,
        InstallationPlanPartitionIdentity BootPartition,
        InstallationPlanPartitionIdentity? RecoveryPartition,
        int FinalSizeGiB,
        long StagingSizeBytes,
        string Username,
        string Hostname,
        string Locale,
        string Keymap,
        string Timezone,
        string DesktopEnvironment = "");

    /// <summary>
    /// Builds a plan from wizard + disk snapshot inputs. Pure except for <c>Guid</c>/clock —
    /// no disk I/O.
    /// </summary>
    public static class InstallationPlanFactory
    {
        public static InstallationPlan Create(
            BuildInstallationPlanRequest request,
            DateTimeOffset? createdAtUtc = null,
            string? planId = null)
        {
            ArgumentNullException.ThrowIfNull(request);

            string id = planId ?? Guid.NewGuid().ToString("N");
            string systemDrive = InstallationTransactionPaths.NormalizeSystemDrive(request.SystemDrive);
            string transactionRoot = InstallationTransactionPaths.GetTransactionRoot(systemDrive, id);

            string firmware = request.IsUefi
                ? InstallationPlanFirmware.Uefi
                : InstallationPlanFirmware.Bios;
            string installMode = request.Mode == InstallMode.Replace
                ? InstallationPlanInstallMode.Replace
                : InstallationPlanInstallMode.DualBoot;

            return new InstallationPlan
            {
                SchemaVersion = InstallationPlan.CurrentSchemaVersion,
                PlanId = id,
                CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow,
                Firmware = firmware,
                InstallMode = installMode,
                Distribution = new InstallationPlanDistribution
                {
                    Id = request.Distro.Id,
                    Name = request.Distro.Name,
                    Family = request.Distro.Family,
                    Version = request.Distro.Version,
                    IsoFileName = Path.GetFileName(request.IsoWindowsPath),
                    IsoUrl = string.IsNullOrWhiteSpace(request.IsoUrl)
                        ? request.Distro.DirectDownloadLink
                        : request.IsoUrl,
                    IsoWindowsPath = Path.GetFullPath(request.IsoWindowsPath),
                    IsoSha256 = (request.IsoSha256 ?? string.Empty).ToLowerInvariant(),
                    IsoSizeBytes = request.IsoSizeBytes,
                },
                Locale = new InstallationPlanLocale
                {
                    Locale = request.Locale,
                    Timezone = request.Timezone,
                    Keymap = request.Keymap,
                    DesktopEnvironment = string.IsNullOrWhiteSpace(request.DesktopEnvironment)
                        ? null
                        : request.DesktopEnvironment,
                },
                Account = new InstallationPlanAccount
                {
                    Username = request.Username.Trim(),
                    PasswordWindowsPath = InstallationTransactionPaths.GetPasswordPath(systemDrive, id),
                    Hostname = request.Hostname.Trim(),
                },
                Disk = new InstallationPlanDisk
                {
                    Number = request.Layout.Index,
                    UniqueId = request.DiskUniqueId,
                    PartitionTableId = request.PartitionTableId,
                    SizeBytes = request.Layout.SizeBytes,
                    LogicalSectorSizeBytes = request.LogicalSectorSizeBytes,
                    PartitionStyle = request.Layout.IsGpt
                        ? InstallationPlanPartitionStyle.Gpt
                        : InstallationPlanPartitionStyle.Mbr,
                    SystemDrive = systemDrive,
                    Windows = request.WindowsPartition,
                    Boot = request.BootPartition,
                    Recovery = request.RecoveryPartition,
                    Installer = new InstallationPlanInstallerPartition
                    {
                        Number = null,
                        OffsetBytes = null,
                        PartitionUuid = null,
                        FinalSizeGiB = request.FinalSizeGiB,
                        StagingSizeBytes = request.StagingSizeBytes,
                    },
                },
                Runtime = new InstallationPlanRuntime
                {
                    TransactionRootWindows = transactionRoot,
                },
            };
        }
    }
}
