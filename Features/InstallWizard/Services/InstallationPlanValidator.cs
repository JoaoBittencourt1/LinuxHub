using System.Text.RegularExpressions;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Cross-field invariants JSON Schema cannot express portably (D2): firmware/layout
    /// parity, ordered non-overlapping geometry, and staging size policy. Pure — no I/O.
    /// </summary>
    public static class InstallationPlanValidator
    {
        private static readonly Regex HexIdPattern = new(
            "^[0-9a-f]{32}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Sha256Pattern = new(
            "^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex AbsoluteWindowsPathPattern = new(
            @"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex GptTableIdPattern = new(
            "^gpt:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MbrTableIdPattern = new(
            "^mbr:[0-9a-f]{8}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static void Validate(InstallationPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            var errors = new List<string>();

            Require(plan.SchemaVersion == InstallationPlan.CurrentSchemaVersion, errors,
                $"schemaVersion must be {InstallationPlan.CurrentSchemaVersion}.");
            Require(HexIdPattern.IsMatch(plan.PlanId ?? string.Empty), errors,
                "planId must contain exactly 32 lowercase hexadecimal characters.");
            Require(plan.CreatedAtUtc != default, errors, "createdAtUtc is required.");
            Require(plan.CreatedAtUtc.Offset == TimeSpan.Zero, errors,
                "createdAtUtc must use UTC.");

            bool isBios = string.Equals(
                plan.Firmware, InstallationPlanFirmware.Bios, StringComparison.Ordinal);
            bool isUefi = string.Equals(
                plan.Firmware, InstallationPlanFirmware.Uefi, StringComparison.Ordinal);
            Require(isBios || isUefi, errors, "firmware must be either 'bios' or 'uefi'.");

            bool isReplace = string.Equals(
                plan.InstallMode, InstallationPlanInstallMode.Replace, StringComparison.Ordinal);
            bool isDualBoot = string.Equals(
                plan.InstallMode, InstallationPlanInstallMode.DualBoot, StringComparison.Ordinal);
            Require(isReplace || isDualBoot, errors,
                "installMode must be either 'replace' or 'dualboot'.");

            ValidateDistribution(plan.Distribution, errors);
            ValidateLocale(plan.Locale, errors);
            ValidateAccount(plan.Account, errors);
            ValidateDisk(plan.Firmware, plan.InstallMode, plan.Disk, errors);
            ValidateRuntime(plan.Runtime, errors);
            ValidateWindowsPathDrives(plan, errors);

            if (errors.Count > 0)
                throw new InstallationPlanValidationException(errors);
        }

        private static void ValidateDistribution(
            InstallationPlanDistribution? distribution,
            ICollection<string> errors)
        {
            if (distribution is null)
            {
                errors.Add("distribution is required.");
                return;
            }

            RequireNotBlank(distribution.Id, "distribution.id", errors);
            RequireNotBlank(distribution.Name, "distribution.name", errors);
            RequireNotBlank(distribution.Family, "distribution.family", errors);
            RequireNotBlank(distribution.Version, "distribution.version", errors);
            RequireNotBlank(distribution.IsoFileName, "distribution.isoFileName", errors);
            Require(
                distribution.IsoFileName.IndexOfAny(['/', '\\']) < 0,
                errors,
                "distribution.isoFileName must be a file name, not a path.");
            RequireNotBlank(distribution.IsoUrl, "distribution.isoUrl", errors);
            Require(
                AbsoluteWindowsPathPattern.IsMatch(distribution.IsoWindowsPath ?? string.Empty),
                errors,
                "distribution.isoWindowsPath must be an absolute Windows drive path.");
            Require(
                Sha256Pattern.IsMatch(distribution.IsoSha256 ?? string.Empty),
                errors,
                "distribution.isoSha256 must contain exactly 64 lowercase hexadecimal characters.");
            RequirePositive(distribution.IsoSizeBytes, "distribution.isoSizeBytes", errors);
        }

        private static void ValidateLocale(
            InstallationPlanLocale? locale,
            ICollection<string> errors)
        {
            if (locale is null)
            {
                errors.Add("locale is required.");
                return;
            }

            RequireNotBlank(locale.Locale, "locale.locale", errors);
            RequireNotBlank(locale.Timezone, "locale.timezone", errors);
            RequireNotBlank(locale.Keymap, "locale.keymap", errors);
        }

        private static void ValidateAccount(
            InstallationPlanAccount? account,
            ICollection<string> errors)
        {
            if (account is null)
            {
                errors.Add("account is required.");
                return;
            }

            RequireNotBlank(account.Username, "account.username", errors);
            Require(
                AbsoluteWindowsPathPattern.IsMatch(account.PasswordWindowsPath ?? string.Empty),
                errors,
                "account.passwordWindowsPath must be an absolute Windows drive path.");
            RequireNotBlank(account.Hostname, "account.hostname", errors);
        }

        private static void ValidateDisk(
            string firmware,
            string installMode,
            InstallationPlanDisk? disk,
            ICollection<string> errors)
        {
            if (disk is null)
            {
                errors.Add("disk is required.");
                return;
            }

            bool isBios = string.Equals(
                firmware, InstallationPlanFirmware.Bios, StringComparison.Ordinal);
            bool isUefi = string.Equals(
                firmware, InstallationPlanFirmware.Uefi, StringComparison.Ordinal);

            Require(disk.Number >= 0, errors, "disk.number cannot be negative.");
            RequireNotBlank(disk.UniqueId, "disk.uniqueId", errors);
            RequirePositive(disk.SizeBytes, "disk.sizeBytes", errors);
            Require(
                disk.LogicalSectorSizeBytes is 512 or 4096,
                errors,
                "disk.logicalSectorSizeBytes must be either 512 or 4096.");
            Require(
                !string.IsNullOrEmpty(disk.SystemDrive) &&
                disk.SystemDrive.Length == 2 &&
                disk.SystemDrive[0] is >= 'A' and <= 'Z' &&
                disk.SystemDrive[1] == ':',
                errors,
                "disk.systemDrive must be an uppercase Windows drive such as C:.");

            if (isBios)
            {
                Require(
                    string.Equals(
                        disk.PartitionStyle,
                        InstallationPlanPartitionStyle.Mbr,
                        StringComparison.Ordinal),
                    errors,
                    "A BIOS plan requires an MBR disk.");
                Require(
                    MbrTableIdPattern.IsMatch(disk.PartitionTableId ?? string.Empty),
                    errors,
                    "disk.partitionTableId must be mbr: followed by 8 hex digits.");
            }
            else if (isUefi)
            {
                Require(
                    string.Equals(
                        disk.PartitionStyle,
                        InstallationPlanPartitionStyle.Gpt,
                        StringComparison.Ordinal),
                    errors,
                    "A UEFI plan requires a GPT disk.");
                Require(
                    GptTableIdPattern.IsMatch(disk.PartitionTableId ?? string.Empty),
                    errors,
                    "disk.partitionTableId must be gpt: followed by a lowercase GUID.");
            }

            if (disk.LogicalSectorSizeBytes is 512 or 4096)
            {
                Require(
                    disk.SizeBytes % disk.LogicalSectorSizeBytes == 0,
                    errors,
                    "disk.sizeBytes must align to disk.logicalSectorSizeBytes.");
            }

            ValidatePartition(disk.Windows, "disk.windows", disk.LogicalSectorSizeBytes, disk.SizeBytes, errors);
            ValidatePartition(disk.Boot, "disk.boot", disk.LogicalSectorSizeBytes, disk.SizeBytes, errors);
            if (disk.Recovery is not null)
            {
                ValidatePartition(
                    disk.Recovery, "disk.recovery", disk.LogicalSectorSizeBytes, disk.SizeBytes, errors);
            }

            ValidateInstaller(disk, installMode, errors);
            ValidateGeometry(disk, errors);
        }

        private static void ValidateInstaller(
            InstallationPlanDisk disk,
            string installMode,
            ICollection<string> errors)
        {
            InstallationPlanInstallerPartition? installer = disk.Installer;
            if (installer is null)
            {
                errors.Add("disk.installer is required.");
                return;
            }

            Require(
                installer.Number.HasValue == installer.OffsetBytes.HasValue,
                errors,
                "disk.installer.number and offsetBytes must either both be set or both be null.");
            Require(
                installer.Number.HasValue == (installer.PartitionUuid is not null),
                errors,
                "disk.installer.partitionUuid must be set exactly when number is set.");

            if (installer.Number is { } number)
                Require(number >= 1, errors, "disk.installer.number must be positive.");
            if (installer.OffsetBytes is { } offset)
            {
                RequirePositive(offset, "disk.installer.offsetBytes", errors);
                if (disk.LogicalSectorSizeBytes is 512 or 4096)
                {
                    Require(
                        offset % disk.LogicalSectorSizeBytes == 0,
                        errors,
                        "disk.installer.offsetBytes must align to disk.logicalSectorSizeBytes.");
                }
            }

            bool isReplace = string.Equals(
                installMode, InstallationPlanInstallMode.Replace, StringComparison.Ordinal);
            bool isDualBoot = string.Equals(
                installMode, InstallationPlanInstallMode.DualBoot, StringComparison.Ordinal);

            if (isDualBoot)
            {
                Require(
                    installer.FinalSizeGiB >= InstallationSizePolicy.MinimumFinalSizeGiB,
                    errors,
                    $"disk.installer.finalSizeGiB must be at least {InstallationSizePolicy.MinimumFinalSizeGiB} for dual-boot.");
                Require(
                    installer.StagingSizeBytes == 0,
                    errors,
                    "disk.installer.stagingSizeBytes must be 0 for dual-boot (no staging partition).");
                Require(
                    !installer.Number.HasValue,
                    errors,
                    "disk.installer identity must remain unset for dual-boot.");
            }
            else if (isReplace)
            {
                Require(
                    installer.FinalSizeGiB >= 0,
                    errors,
                    "disk.installer.finalSizeGiB cannot be negative.");
                RequirePositive(
                    installer.StagingSizeBytes,
                    "disk.installer.stagingSizeBytes",
                    errors);
            }
        }

        private static void ValidateGeometry(InstallationPlanDisk disk, ICollection<string> errors)
        {
            if (disk.Windows is null || disk.Boot is null || disk.SizeBytes <= 0)
                return;

            var named = new List<(string Name, InstallationPlanPartitionIdentity Partition)>
            {
                ("disk.windows", disk.Windows),
                ("disk.boot", disk.Boot),
            };
            if (disk.Recovery is not null)
                named.Add(("disk.recovery", disk.Recovery));

            for (int left = 0; left < named.Count; left++)
            {
                for (int right = left + 1; right < named.Count; right++)
                {
                    var a = named[left].Partition;
                    var b = named[right].Partition;
                    long aEnd = a.OffsetBytes + a.SizeBytes;
                    long bEnd = b.OffsetBytes + b.SizeBytes;
                    bool overlap = a.OffsetBytes < bEnd && b.OffsetBytes < aEnd;
                    Require(
                        !overlap,
                        errors,
                        $"{named[left].Name} and {named[right].Name} overlap.");
                }
            }

            if (disk.Recovery is not null)
            {
                long windowsEnd = disk.Windows.OffsetBytes + disk.Windows.SizeBytes;
                Require(
                    windowsEnd <= disk.Recovery.OffsetBytes,
                    errors,
                    "disk.recovery must start at or after the original Windows partition end.");
            }

            if (disk.Installer?.OffsetBytes is not { } stagingOffset ||
                disk.Installer.StagingSizeBytes <= 0)
            {
                return;
            }

            long stagingEnd = stagingOffset + disk.Installer.StagingSizeBytes;
            foreach ((string name, InstallationPlanPartitionIdentity partition) in named)
            {
                long end = partition.OffsetBytes + partition.SizeBytes;
                bool overlapsStaging =
                    stagingOffset < end && partition.OffsetBytes < stagingEnd;
                Require(
                    !overlapsStaging,
                    errors,
                    $"disk.installer staging extent would overlap {name}.");
            }
        }

        private static void ValidatePartition(
            InstallationPlanPartitionIdentity? partition,
            string name,
            int logicalSectorSizeBytes,
            long diskSizeBytes,
            ICollection<string> errors)
        {
            if (partition is null)
            {
                errors.Add($"{name} is required.");
                return;
            }

            Require(partition.Number >= 1, errors, $"{name}.number must be positive.");
            RequirePositive(partition.OffsetBytes, $"{name}.offsetBytes", errors);
            RequirePositive(partition.SizeBytes, $"{name}.sizeBytes", errors);
            Require(
                partition.OffsetBytes <= diskSizeBytes - partition.SizeBytes,
                errors,
                $"{name} must fit within disk.sizeBytes.");

            if (logicalSectorSizeBytes is 512 or 4096)
            {
                Require(
                    partition.OffsetBytes % logicalSectorSizeBytes == 0,
                    errors,
                    $"{name}.offsetBytes must align to disk.logicalSectorSizeBytes.");
                Require(
                    partition.SizeBytes % logicalSectorSizeBytes == 0,
                    errors,
                    $"{name}.sizeBytes must align to disk.logicalSectorSizeBytes.");
            }
        }

        private static void ValidateRuntime(
            InstallationPlanRuntime? runtime,
            ICollection<string> errors)
        {
            if (runtime is null)
            {
                errors.Add("runtime is required.");
                return;
            }

            Require(
                AbsoluteWindowsPathPattern.IsMatch(runtime.TransactionRootWindows ?? string.Empty),
                errors,
                "runtime.transactionRootWindows must be an absolute Windows drive path.");

            if (runtime.EncryptionPercentComplete is { } percent)
            {
                Require(
                    percent is >= 0 and <= 100,
                    errors,
                    "runtime.encryptionPercentComplete must be between 0 and 100.");
            }
        }

        private static void ValidateWindowsPathDrives(
            InstallationPlan plan,
            ICollection<string> errors)
        {
            string? systemDrive = plan.Disk?.SystemDrive;
            if (string.IsNullOrEmpty(systemDrive) || systemDrive.Length != 2)
                return;

            ValidateDrive(
                plan.Distribution?.IsoWindowsPath,
                "distribution.isoWindowsPath",
                systemDrive,
                errors);
            ValidateDrive(
                plan.Account?.PasswordWindowsPath,
                "account.passwordWindowsPath",
                systemDrive,
                errors);
            ValidateDrive(
                plan.Runtime?.TransactionRootWindows,
                "runtime.transactionRootWindows",
                systemDrive,
                errors);
        }

        private static void ValidateDrive(
            string? path,
            string pathName,
            string systemDrive,
            ICollection<string> errors)
        {
            if (!AbsoluteWindowsPathPattern.IsMatch(path ?? string.Empty))
                return;

            Require(
                string.Equals(path![..2], systemDrive, StringComparison.OrdinalIgnoreCase),
                errors,
                $"{pathName} must be located on disk.systemDrive.");
        }

        private static void RequireNotBlank(string? value, string name, ICollection<string> errors) =>
            Require(!string.IsNullOrWhiteSpace(value), errors, $"{name} is required.");

        private static void RequirePositive(long value, string name, ICollection<string> errors) =>
            Require(value > 0, errors, $"{name} must be positive.");

        private static void Require(bool condition, ICollection<string> errors, string message)
        {
            if (!condition)
                errors.Add(message);
        }
    }

    public sealed class InstallationPlanValidationException : Exception
    {
        public InstallationPlanValidationException(IReadOnlyCollection<string> errors)
            : base("The installation plan is invalid: " + string.Join(" ", errors))
        {
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        public IReadOnlyCollection<string> Errors { get; }
    }
}
