using System.Text.Json.Serialization;

namespace LinuxHub.Features.InstallWizard.Models
{
    /// <summary>
    /// Projection of <c>schemas/installation-plan.schema.json</c> (D1). Published atomically
    /// before any disk mutation; semantic invariants live in
    /// <see cref="Services.InstallationPlanValidator"/> (D2).
    /// </summary>
    public sealed class InstallationPlan
    {
        public const int CurrentSchemaVersion = 1;

        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonPropertyName("planId")]
        public string PlanId { get; set; } = string.Empty;

        [JsonPropertyName("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; set; }

        [JsonPropertyName("firmware")]
        public string Firmware { get; set; } = string.Empty;

        [JsonPropertyName("installMode")]
        public string InstallMode { get; set; } = string.Empty;

        [JsonPropertyName("distribution")]
        public InstallationPlanDistribution Distribution { get; set; } = new();

        [JsonPropertyName("locale")]
        public InstallationPlanLocale Locale { get; set; } = new();

        [JsonPropertyName("account")]
        public InstallationPlanAccount Account { get; set; } = new();

        [JsonPropertyName("disk")]
        public InstallationPlanDisk Disk { get; set; } = new();

        [JsonPropertyName("runtime")]
        public InstallationPlanRuntime Runtime { get; set; } = new();
    }

    public static class InstallationPlanFirmware
    {
        public const string Bios = "bios";
        public const string Uefi = "uefi";
    }

    public static class InstallationPlanInstallMode
    {
        public const string Replace = "replace";
        public const string DualBoot = "dualboot";
    }

    public static class InstallationPlanPartitionStyle
    {
        public const string Mbr = "MBR";
        public const string Gpt = "GPT";
    }

    public sealed class InstallationPlanDistribution
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("family")]
        public string Family { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("isoFileName")]
        public string IsoFileName { get; set; } = string.Empty;

        [JsonPropertyName("isoUrl")]
        public string IsoUrl { get; set; } = string.Empty;

        [JsonPropertyName("isoWindowsPath")]
        public string IsoWindowsPath { get; set; } = string.Empty;

        [JsonPropertyName("isoSha256")]
        public string IsoSha256 { get; set; } = string.Empty;

        [JsonPropertyName("isoSizeBytes")]
        public long IsoSizeBytes { get; set; }
    }

    public sealed class InstallationPlanLocale
    {
        [JsonPropertyName("locale")]
        public string Locale { get; set; } = string.Empty;

        [JsonPropertyName("timezone")]
        public string Timezone { get; set; } = string.Empty;

        [JsonPropertyName("keymap")]
        public string Keymap { get; set; } = string.Empty;

        [JsonPropertyName("desktopEnvironment")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DesktopEnvironment { get; set; }
    }

    public sealed class InstallationPlanAccount
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("passwordWindowsPath")]
        public string PasswordWindowsPath { get; set; } = string.Empty;

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = string.Empty;
    }

    public sealed class InstallationPlanDisk
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("uniqueId")]
        public string UniqueId { get; set; } = string.Empty;

        [JsonPropertyName("partitionTableId")]
        public string PartitionTableId { get; set; } = string.Empty;

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("logicalSectorSizeBytes")]
        public int LogicalSectorSizeBytes { get; set; }

        [JsonPropertyName("partitionStyle")]
        public string PartitionStyle { get; set; } = string.Empty;

        [JsonPropertyName("systemDrive")]
        public string SystemDrive { get; set; } = string.Empty;

        [JsonPropertyName("windows")]
        public InstallationPlanPartitionIdentity Windows { get; set; } = new();

        [JsonPropertyName("boot")]
        public InstallationPlanPartitionIdentity Boot { get; set; } = new();

        [JsonPropertyName("recovery")]
        public InstallationPlanPartitionIdentity? Recovery { get; set; }

        [JsonPropertyName("installer")]
        public InstallationPlanInstallerPartition Installer { get; set; } = new();
    }

    public sealed class InstallationPlanPartitionIdentity
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("offsetBytes")]
        public long OffsetBytes { get; set; }

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }
    }

    /// <summary>
    /// Observed staging identity (<see cref="Number"/>/<see cref="OffsetBytes"/>/
    /// <see cref="PartitionUuid"/>) may be null until the staging partition is created —
    /// the only post-publish mutation allowed (spec). Policy sizes stay in GiB (D3);
    /// staging size is exact bytes because it is derived from the ISO, not a user GiB choice.
    /// </summary>
    public sealed class InstallationPlanInstallerPartition
    {
        [JsonPropertyName("number")]
        public int? Number { get; set; }

        [JsonPropertyName("offsetBytes")]
        public long? OffsetBytes { get; set; }

        [JsonPropertyName("partitionUuid")]
        public string? PartitionUuid { get; set; }

        [JsonPropertyName("finalSizeGiB")]
        public int FinalSizeGiB { get; set; }

        [JsonPropertyName("stagingSizeBytes")]
        public long StagingSizeBytes { get; set; }
    }

    public sealed class InstallationPlanRuntime
    {
        [JsonPropertyName("transactionRootWindows")]
        public string TransactionRootWindows { get; set; } = string.Empty;

        [JsonPropertyName("encryptionConversionStatus")]
        public string? EncryptionConversionStatus { get; set; }

        [JsonPropertyName("encryptionPercentComplete")]
        public double? EncryptionPercentComplete { get; set; }
    }
}
