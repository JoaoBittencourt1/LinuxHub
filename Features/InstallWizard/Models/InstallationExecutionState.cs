using System.Text.Json.Serialization;

namespace LinuxHub.Features.InstallWizard.Models
{
    /// <summary>
    /// Durable snapshot of completed and compensated installation operations.
    /// Projection of <c>schemas/installation-state.schema.json</c>.
    /// </summary>
    public sealed class InstallationExecutionState
    {
        public const int CurrentSchemaVersion = 1;

        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonPropertyName("planId")]
        public string PlanId { get; set; } = string.Empty;

        [JsonPropertyName("revision")]
        public int Revision { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = InstallationStatus.Pending;

        [JsonPropertyName("phase")]
        public string Phase { get; set; } = InstallationPhase.Windows;

        [JsonPropertyName("activeStep")]
        public string? ActiveStep { get; set; }

        [JsonPropertyName("completedSteps")]
        public List<string> CompletedSteps { get; set; } = [];

        [JsonPropertyName("compensatedSteps")]
        public List<string> CompensatedSteps { get; set; } = [];

        [JsonPropertyName("failure")]
        public InstallationFailure? Failure { get; set; }

        [JsonPropertyName("progress")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public InstallationProgress? Progress { get; set; }

        [JsonPropertyName("updatedAtUtc")]
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    public sealed class InstallationProgress
    {
        [JsonPropertyName("stage")]
        public string Stage { get; set; } = string.Empty;

        [JsonPropertyName("overallPercent")]
        public int OverallPercent { get; set; }

        [JsonPropertyName("detailPercent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? DetailPercent { get; set; }
    }

    public sealed class InstallationFailure
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("component")]
        public string Component { get; set; } = string.Empty;
    }

    public static class InstallationStatus
    {
        public const string Pending = "pending";
        public const string Running = "running";
        public const string Failed = "failed";
        public const string RollbackRunning = "rollback-running";
        public const string RolledBack = "rolled-back";
        public const string Succeeded = "succeeded";
    }

    public static class InstallationPhase
    {
        public const string Windows = "windows";
        public const string Live = "live";
        public const string Target = "target";
        public const string Rollback = "rollback";
        public const string Complete = "complete";
    }

    public static class InstallationStepIds
    {
        public const string WindowsPlanPublished = "windows.plan-published";
        public const string WindowsDiskPrepared = "windows.disk-prepared";
        public const string WindowsStagingPrepared = "windows.staging-prepared";
        public const string WindowsInstallerConfigWritten = "windows.installer-config-written";
        public const string WindowsTemporaryBootPrepared = "windows.temporary-boot-prepared";

        // own-linux-installer task 9.1: os quatro reservados pelo D13.2 do change anterior
        // agora Armed=true (InstallationStepCatalog).
        public const string LiveIsoMounted = "live.iso-mounted";
        public const string LiveDistributionExtracted = "live.distribution-extracted";
        public const string TargetSystemConfigured = "target.system-configured";

        /// <summary>
        /// own-linux-installer: a cadeia de boot assinada é INSTALADA no alvo, a partir do
        /// repositório apt que a própria ISO carrega, antes de ser copiada para a ESP.
        ///
        /// É um passo próprio porque é uma unidade de trabalho que pode falhar sozinha e
        /// significa algo distinto: o squashfs de uma ISO live do Debian traz o kernel mas não
        /// o bootloader (só <c>grub-common</c>; <c>grub2-common</c>, <c>grub-efi-amd64-signed</c>
        /// e <c>shim-signed</c> vivem no <c>pool/</c>). Sem separar, "falhou ao instalar o grub"
        /// e "falhou ao escrever na ESP" apareceriam como o mesmo passo incompleto no registro.
        /// </summary>
        public const string TargetBootloaderPackagesInstalled = "target.bootloader-packages-installed";

        public const string TargetBootloaderInstalled = "target.bootloader-installed";

        // own-linux-installer task 9.2 (design.md D12): sem este passo, "instalado" significa
        // só "os comandos rodaram". Required, não compensável — verifica o resultado antes de
        // a instalação ser declarada concluída.
        public const string TargetInstallationVerified = "target.installation-verified";
    }
}
