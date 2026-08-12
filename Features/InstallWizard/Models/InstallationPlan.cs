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

        /// <summary>
        /// own-linux-installer: qual mecanismo desatendido esta transação usa, como string
        /// (o nome do valor de <see cref="LinuxHub.Common.Models.UnattendedInstallMechanism"/>).
        ///
        /// Existe porque o validador precisa distinguir dois planos de dual-boot que são
        /// legítimos e mutuamente exclusivos: no caminho do instalador nativo, quem cria a
        /// partição raiz é o instalador da distro, então <c>disk.installer</c> NÃO pode ter
        /// identidade; no caminho do instalador próprio, o app cria a partição antes do reboot
        /// e a identidade é obrigatória — é ela que o instalador live lê para saber onde
        /// escrever (memória do projeto: "ler, nunca deduzir o disco").
        /// </summary>
        [JsonPropertyName("unattendedMechanism")]
        public string UnattendedMechanism { get; set; } =
            nameof(LinuxHub.Common.Models.UnattendedInstallMechanism.None);

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

        /// <summary>
        /// own-linux-installer task 2.6 (design.md D6): identidade de distribuição esperada
        /// DENTRO do artefato — o valor de <c>ID=</c> em <c>/etc/os-release</c> no squashfs. Só
        /// preenchido para o mecanismo <see cref="LinuxHub.Common.Models.UnattendedInstallMechanism.OwnLiveInstaller"/>;
        /// os caminhos preservados não extraem nada e não precisam desta verificação. O nome da
        /// distro nunca seleciona caminho de código (§2) — este campo só alimenta uma
        /// comparação de igualdade do lado live, nunca um <c>if</c> por identidade.
        /// </summary>
        [JsonPropertyName("expectedIdentity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string ExpectedIdentity { get; set; } = string.Empty;
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

        /// <summary>
        /// own-linux-installer task 2.6 (design.md D5): caminho relativo, dentro da ESP, do
        /// espaço temporário que o boot staging cria só para chegar até a mídia live própria.
        /// A instalação live remove este diretório no fim — mas só depois de provar, pelo
        /// marcador de posse (o próprio <see cref="InstallationPlan.PlanId"/>), que ele
        /// pertence à transação corrente (D5). Vazio nos caminhos preservados, que não usam a
        /// mídia live e não criam este espaço.
        /// </summary>
        [JsonPropertyName("stagingEspDirectory")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? StagingEspDirectory { get; set; }
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
