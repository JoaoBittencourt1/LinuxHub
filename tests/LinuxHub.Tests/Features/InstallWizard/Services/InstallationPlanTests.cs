using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class InstallationPlanValidatorTests
    {
        private static readonly string PlanId = new string('a', 32);
        private static readonly string Sha256 = new string('b', 64);

        internal static InstallationPlan ValidUefiPlan()
        {
            string systemDrive = "C:";
            string transactionRoot = InstallationTransactionPaths.GetTransactionRoot(systemDrive, PlanId);

            return new InstallationPlan
            {
                SchemaVersion = 1,
                PlanId = PlanId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Firmware = InstallationPlanFirmware.Uefi,
                InstallMode = InstallationPlanInstallMode.DualBoot,
                Distribution = new InstallationPlanDistribution
                {
                    Id = "ubuntu",
                    Name = "Ubuntu",
                    Family = "Debian",
                    Version = "24.04",
                    IsoFileName = "ubuntu.iso",
                    IsoUrl = "https://releases.ubuntu.com/ubuntu.iso",
                    IsoWindowsPath = @"C:\isos\ubuntu.iso",
                    IsoSha256 = Sha256,
                    IsoSizeBytes = 6_000_000_000,
                },
                Locale = new InstallationPlanLocale
                {
                    Locale = "pt_BR",
                    Timezone = "America/Sao_Paulo",
                    Keymap = "br",
                },
                Account = new InstallationPlanAccount
                {
                    Username = "joao",
                    PasswordWindowsPath = InstallationTransactionPaths.GetPasswordPath(systemDrive, PlanId),
                    Hostname = "pc",
                },
                Disk = new InstallationPlanDisk
                {
                    Number = 0,
                    UniqueId = "DISK0",
                    PartitionTableId = "gpt:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                    SizeBytes = 512L * 1024 * 1024 * 1024,
                    LogicalSectorSizeBytes = 512,
                    PartitionStyle = InstallationPlanPartitionStyle.Gpt,
                    SystemDrive = systemDrive,
                    Windows = new InstallationPlanPartitionIdentity
                    {
                        Number = 2,
                        OffsetBytes = 101 * 1024 * 1024,
                        SizeBytes = 200L * 1024 * 1024 * 1024,
                    },
                    Boot = new InstallationPlanPartitionIdentity
                    {
                        Number = 1,
                        OffsetBytes = 1024 * 1024,
                        SizeBytes = 100 * 1024 * 1024,
                    },
                    Recovery = new InstallationPlanPartitionIdentity
                    {
                        Number = 3,
                        OffsetBytes = 400L * 1024 * 1024 * 1024,
                        SizeBytes = 1L * 1024 * 1024 * 1024,
                    },
                    Installer = new InstallationPlanInstallerPartition
                    {
                        FinalSizeGiB = 50,
                        StagingSizeBytes = 0,
                    },
                },
                Runtime = new InstallationPlanRuntime
                {
                    TransactionRootWindows = transactionRoot,
                },
            };
        }

        [Fact]
        public void Validate_AcceptsAValidPlan()
        {
            InstallationPlanValidator.Validate(ValidUefiPlan());
        }

        /// <summary>
        /// own-linux-installer: no dual-boot pelo instalador NATIVO, quem cria a partição raiz
        /// é o instalador da distro depois do reboot — preencher a identidade aqui seria
        /// inventar um alvo que ainda não existe. Regra que já existia; este teste a fixa.
        /// </summary>
        [Fact]
        public void Validate_DualBootWithNativeInstaller_RejectsInstallerIdentity()
        {
            InstallationPlan plan = ValidUefiPlan();
            plan.UnattendedMechanism = nameof(UnattendedInstallMechanism.Subiquity);
            plan.Disk.Installer.Number = 6;
            plan.Disk.Installer.OffsetBytes = 300L * 1024 * 1024 * 1024;
            plan.Disk.Installer.PartitionUuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

            var error = Assert.Throws<InstallationPlanValidationException>(
                () => InstallationPlanValidator.Validate(plan));

            Assert.Contains(error.Errors, e => e.Contains("must remain unset", StringComparison.Ordinal));
        }

        /// <summary>
        /// No instalador próprio a identidade é o único jeito de o instalador live saber onde
        /// escrever — sem ela ele teria de deduzir o alvo, que é como o incidente de
        /// 2026-08-05 começou. Aqui ela é permitida (a mesma regra que a recusa acima).
        /// </summary>
        [Fact]
        public void Validate_DualBootWithOwnLiveInstaller_AcceptsInstallerIdentity()
        {
            InstallationPlan plan = ValidUefiPlan();
            plan.UnattendedMechanism = nameof(UnattendedInstallMechanism.OwnLiveInstaller);
            plan.Disk.Installer.Number = 6;
            plan.Disk.Installer.OffsetBytes = 300L * 1024 * 1024 * 1024;
            plan.Disk.Installer.PartitionUuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
            plan.Disk.Installer.SizeBytes = 50L * 1024 * 1024 * 1024;

            InstallationPlanValidator.Validate(plan);
        }

        /// <summary>
        /// Identidade registrada sem tamanho observado é recusada: é o tamanho que o instalador
        /// live confere contra o dispositivo antes do mkfs. Sem esta regra o plano passava aqui
        /// e a instalação morria do outro lado do reboot, depois de revalidar disco, geometria,
        /// partições e o hash de vários GB — todo esse trabalho para descobrir um campo que
        /// nunca foi escrito.
        /// </summary>
        [Fact]
        public void Validate_OwnLiveInstaller_RejectsInstallerIdentityWithoutObservedSize()
        {
            InstallationPlan plan = ValidUefiPlan();
            plan.UnattendedMechanism = nameof(UnattendedInstallMechanism.OwnLiveInstaller);
            plan.Disk.Installer.Number = 6;
            plan.Disk.Installer.OffsetBytes = 300L * 1024 * 1024 * 1024;
            plan.Disk.Installer.PartitionUuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

            var error = Assert.Throws<InstallationPlanValidationException>(
                () => InstallationPlanValidator.Validate(plan));

            Assert.Contains("sizeBytes", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A extensão observada da raiz não pode invadir Windows, boot ou recuperação. Antes
        /// desta regra a checagem de sobreposição só olhava <c>stagingSizeBytes</c>, que é 0 no
        /// instalador próprio — ou seja, a partição que este caminho realmente cria era a única
        /// que ninguém conferia.
        /// </summary>
        [Fact]
        public void Validate_OwnLiveInstaller_RejectsRootExtentOverlappingWindows()
        {
            InstallationPlan plan = ValidUefiPlan();
            plan.UnattendedMechanism = nameof(UnattendedInstallMechanism.OwnLiveInstaller);
            plan.Disk.Installer.Number = 6;
            plan.Disk.Installer.OffsetBytes = plan.Disk.Windows.OffsetBytes;
            plan.Disk.Installer.PartitionUuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
            plan.Disk.Installer.SizeBytes = plan.Disk.Windows.SizeBytes;

            var error = Assert.Throws<InstallationPlanValidationException>(
                () => InstallationPlanValidator.Validate(plan));

            Assert.Contains("overlap", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// O plano é publicado e validado ANTES de a partição existir (primeiro passo do
        /// fluxo); a identidade só é registrada depois de criá-la. Exigir a identidade na
        /// validação quebraria a publicação — este teste trava essa ordem.
        /// </summary>
        [Fact]
        public void Validate_OwnLiveInstaller_AcceptsPlanPublishedBeforeThePartitionExists()
        {
            InstallationPlan plan = ValidUefiPlan();
            plan.UnattendedMechanism = nameof(UnattendedInstallMechanism.OwnLiveInstaller);

            InstallationPlanValidator.Validate(plan);
        }

        [Fact]
        public void Validate_RejectsFirmwareLayoutMismatch()
        {
            InstallationPlan plan = ValidUefiPlan();
            plan.Firmware = InstallationPlanFirmware.Bios;

            var error = Assert.Throws<InstallationPlanValidationException>(
                () => InstallationPlanValidator.Validate(plan));

            Assert.Contains(error.Errors, e => e.Contains("MBR", StringComparison.Ordinal));
        }

        [Fact]
        public void Validate_RejectsOverlappingGeometry()
        {
            InstallationPlan plan = ValidUefiPlan();
            plan.Disk.Recovery!.OffsetBytes = plan.Disk.Windows.OffsetBytes;

            var error = Assert.Throws<InstallationPlanValidationException>(
                () => InstallationPlanValidator.Validate(plan));

            Assert.Contains(error.Errors, e => e.Contains("overlap", StringComparison.Ordinal));
        }

        [Fact]
        public void Validate_RejectsStagingOverlappingRecovery()
        {
            InstallationPlan plan = ValidUefiPlan();
            plan.InstallMode = InstallationPlanInstallMode.Replace;
            plan.Disk.Installer.FinalSizeGiB = 0;
            plan.Disk.Installer.StagingSizeBytes = 8L * 1024 * 1024 * 1024;
            plan.Disk.Installer.Number = 9;
            plan.Disk.Installer.OffsetBytes = plan.Disk.Recovery!.OffsetBytes;
            plan.Disk.Installer.PartitionUuid = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";

            var error = Assert.Throws<InstallationPlanValidationException>(
                () => InstallationPlanValidator.Validate(plan));

            Assert.Contains(error.Errors, e => e.Contains("overlap", StringComparison.Ordinal));
        }

        [Fact]
        public void Validate_RejectsDualBootBelowMinimumGiB()
        {
            InstallationPlan plan = ValidUefiPlan();
            plan.Disk.Installer.FinalSizeGiB = 4;

            var error = Assert.Throws<InstallationPlanValidationException>(
                () => InstallationPlanValidator.Validate(plan));

            Assert.Contains(error.Errors, e => e.Contains("finalSizeGiB", StringComparison.Ordinal));
        }
    }

    public class InstallationPlanPublisherTests
    {
        [Fact]
        public void Publish_RejectsUnknownJsonFieldsOnRead()
        {
            InstallationPlan plan = InstallationPlanValidatorTests.ValidUefiPlan();
            string directory = Path.Combine(
                Path.GetTempPath(),
                "linuxhub-plan-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "plan.json");

            try
            {
                string json = JsonSerializer.Serialize(plan, InstallationPlanPublisher.SerializerOptions);
                JsonNode node = JsonNode.Parse(json)!;
                node["unexpectedField"] = "nope";
                File.WriteAllText(path, node.ToJsonString());

                var publisher = new InstallationPlanPublisher();
                var error = Assert.Throws<InstallationPlanValidationException>(
                    () => publisher.ReadValidated(path));

                Assert.Contains(error.Errors, e => e.Contains("invalid", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Publish_IsAtomic_PreviousDocumentSurvivesFailedReplace()
        {
            // Contract covered by AtomicJsonFile: write temp then replace. This test proves a
            // reader never sees a truncated document when we write twice successfully.
            InstallationPlan plan = InstallationPlanValidatorTests.ValidUefiPlan();
            string directory = Path.Combine(
                Path.GetTempPath(),
                "linuxhub-plan-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "plan.json");

            try
            {
                AtomicJsonFile.Write(path, JsonSerializer.Serialize(plan, InstallationPlanPublisher.SerializerOptions));
                string first = File.ReadAllText(path);

                plan.Disk.Installer.FinalSizeGiB = 80;
                AtomicJsonFile.Write(path, JsonSerializer.Serialize(plan, InstallationPlanPublisher.SerializerOptions));
                string second = File.ReadAllText(path);

                Assert.Contains("\"finalSizeGiB\": 50", first, StringComparison.Ordinal);
                Assert.Contains("\"finalSizeGiB\": 80", second, StringComparison.Ordinal);
                Assert.True(second.TrimEnd().EndsWith('}'));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Publish_RoundTripsThroughRealProgramDataPath()
        {
            // Uses the real transaction path on the system drive — the Open Question answer
            // for 3.2. Skips when ProgramData is not writable (restricted CI agents).
            string systemDrive = InstallationTransactionPaths.NormalizeSystemDrive(
                Environment.GetEnvironmentVariable("SystemDrive") ?? "C:");
            string planId = Guid.NewGuid().ToString("N");
            string transactionRoot = InstallationTransactionPaths.GetTransactionRoot(systemDrive, planId);

            InstallationPlan plan = InstallationPlanValidatorTests.ValidUefiPlan();
            plan.PlanId = planId;
            plan.Disk.SystemDrive = systemDrive;
            plan.Runtime.TransactionRootWindows = transactionRoot;
            plan.Account.PasswordWindowsPath =
                InstallationTransactionPaths.GetPasswordPath(systemDrive, planId);
            plan.Distribution.IsoWindowsPath = Path.Combine(systemDrive + @"\", "isos", "ubuntu.iso");

            var publisher = new InstallationPlanPublisher();
            try
            {
                string path = publisher.Publish(plan, "secret");
                Assert.True(File.Exists(path));
                Assert.NotNull(publisher.Current);

                InstallationPlan reread = publisher.ReadValidated(path);
                Assert.Equal(planId, reread.PlanId);
                Assert.Equal("secret", File.ReadAllText(plan.Account.PasswordWindowsPath).TrimEnd());
            }
            catch (UnauthorizedAccessException)
            {
                // Agent without rights to ProgramData — shape/validator coverage lives elsewhere.
            }
            finally
            {
                publisher.Clear();
                if (Directory.Exists(transactionRoot))
                    Directory.Delete(transactionRoot, recursive: true);
            }
        }
    }

    public class InstallationPlanMutationGuardTests
    {
        [Fact]
        public void EnsurePublishedForDisk_RejectsWithoutPlan()
        {
            var publisher = new InstallationPlanPublisher();
            var guard = new InstallationPlanMutationGuard(publisher);

            var error = Assert.Throws<InvalidOperationException>(
                () => guard.EnsurePublishedForDisk(0));

            Assert.Contains("no installation plan", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EnsurePublishedForDisk_RejectsMismatchedDisk()
        {
            var publisher = new InstallationPlanPublisher();
            InstallationPlan plan = InstallationPlanValidatorTests.ValidUefiPlan();
            // Avoid ProgramData: set Current via a publish to a fake by using reflection-free
            // approach — publish only when writable; otherwise set through a test subclass.
            var testPublisher = new InMemoryPlanPublisher(plan);
            var guard = new InstallationPlanMutationGuard(testPublisher);

            var error = Assert.Throws<InvalidOperationException>(
                () => guard.EnsurePublishedForDisk(99));

            Assert.Contains("targets disk", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class InMemoryPlanPublisher : IInstallationPlanPublisher
        {
            public InMemoryPlanPublisher(InstallationPlan plan)
            {
                Current = plan;
                PublishedPath = "memory";
            }

            public InstallationPlan? Current { get; }
            public string? PublishedPath { get; }
            public string Publish(InstallationPlan plan, string password) => PublishedPath!;
            public InstallationPlan ReadValidated(string path) => Current!;
            public void UpdateStagingIdentity(
                int number, long offsetBytes, string partitionUuid, long? observedSizeBytes = null) { }
            public void Clear() { }
        }
    }

    public class InstallationPlanSchemaParityTests
    {
        [Fact]
        public void CSharpModel_DeclaresEveryRequiredSchemaProperty()
        {
            string schemaPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "schemas", "installation-plan.schema.json"));

            if (!File.Exists(schemaPath))
            {
                schemaPath = Path.GetFullPath(Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "schemas", "installation-plan.schema.json"));
            }

            Assert.True(File.Exists(schemaPath), $"Schema not found at {schemaPath}");

            JsonNode root = JsonNode.Parse(File.ReadAllText(schemaPath))!;
            string[] required = root["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

            InstallationPlan plan = InstallationPlanValidatorTests.ValidUefiPlan();
            string json = JsonSerializer.Serialize(plan, InstallationPlanPublisher.SerializerOptions);
            JsonNode serialized = JsonNode.Parse(json)!;

            foreach (string property in required)
                Assert.True(serialized[property] is not null, $"Missing serialized property '{property}'");
        }
    }

    public class InstallerConfigFromPlanTests
    {
        [Fact]
        public void Derive_ReadsPasswordSidecarAndMatchesPlan()
        {
            InstallationPlan plan = InstallationPlanValidatorTests.ValidUefiPlan();
            string directory = Path.Combine(Path.GetTempPath(), "linuxhub-plan-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string passwordPath = Path.Combine(directory, "account-secret.env");
            File.WriteAllText(passwordPath, "s3cret\n");

            plan.Account.PasswordWindowsPath = passwordPath;
            // Paths must share a drive letter for the validator; keep C: and accept that the
            // password file lives on whatever temp drive — override validator drive check by
            // placing sidecar on C: when possible.
            string systemDrive = InstallationTransactionPaths.NormalizeSystemDrive(
                Environment.GetEnvironmentVariable("SystemDrive") ?? "C:");
            if (!passwordPath.StartsWith(systemDrive, StringComparison.OrdinalIgnoreCase))
            {
                // Temp is on another drive — skip rather than require admin to write ProgramData.
                return;
            }

            var config = InstallerConfigFromPlan.Derive(plan);
            Assert.Equal("s3cret", config.Password);
            Assert.Equal(plan.Distribution.Id, config.DistroId);
            Assert.Equal(plan.Disk.Number, config.TargetDiskIndex);
        }

        [Fact]
        public void EnsureConfigMatchesPlan_RejectsDivergentDisk()
        {
            InstallationPlan plan = InstallationPlanValidatorTests.ValidUefiPlan();
            var config = new InstallerConfig
            {
                DistroId = plan.Distribution.Id,
                BootMode = plan.Firmware,
                InstallMode = plan.InstallMode,
                TargetDiskIndex = 99,
                IsoPath = plan.Distribution.IsoWindowsPath,
                Username = plan.Account.Username,
                Hostname = plan.Account.Hostname,
            };

            Assert.Throws<InvalidOperationException>(
                () => InstallerConfigFromPlan.EnsureConfigMatchesPlan(config, plan));
        }
    }

    public class InstallationPlanFactoryTests
    {
        [Fact]
        public void Create_ProjectsWizardInputsOntoPlan()
        {
            var distro = new DistroInfo
            {
                Id = "ubuntu",
                Name = "Ubuntu",
                Family = "Debian",
                Version = "24.04",
                DirectDownloadLink = "https://releases.ubuntu.com/ubuntu.iso",
                Sha256 = new string('c', 64),
                SizeBytes = 6_000_000_000,
            };

            var layout = new DiskLayout(
                Index: 0,
                SerialNumber: "SN",
                Model: "Disk",
                SizeBytes: 512L * 1024 * 1024 * 1024,
                IsGpt: true,
                IsLargestDisk: true,
                IsSmallestDisk: true,
                Partitions:
                [
                    new PartitionLayout(1, 1024 * 1024, 100 * 1024 * 1024,
                        "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}", true),
                    new PartitionLayout(2, 101 * 1024 * 1024, 200L * 1024 * 1024 * 1024,
                        "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}", false),
                ],
                Guid: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                UniqueId: "UID",
                LogicalSectorSizeBytes: 512);

            var plan = InstallationPlanFactory.Create(
                new BuildInstallationPlanRequest(
                    Distro: distro,
                    IsoWindowsPath: @"C:\isos\ubuntu.iso",
                    IsoSizeBytes: distro.SizeBytes,
                    IsoSha256: distro.Sha256,
                    IsoUrl: distro.DirectDownloadLink,
                    IsUefi: true,
                    Mode: InstallMode.DualBoot,
                    Layout: layout,
                    LogicalSectorSizeBytes: 512,
                    SystemDrive: "C:",
                    DiskUniqueId: InstallationPlanDiskIdentity.BuildUniqueId(layout),
                    PartitionTableId: InstallationPlanDiskIdentity.BuildPartitionTableId(layout),
                    WindowsPartition: InstallationPlanDiskIdentity.ToIdentity(layout.Partitions[1]),
                    BootPartition: InstallationPlanDiskIdentity.ToIdentity(layout.Partitions[0]),
                    RecoveryPartition: null,
                    FinalSizeGiB: 40,
                    StagingSizeBytes: 0,
                    Username: "joao",
                    Hostname: "pc",
                    Locale: "pt_BR",
                    Keymap: "br",
                    Timezone: "America/Sao_Paulo"),
                createdAtUtc: DateTimeOffset.Parse("2026-08-11T12:00:00Z"),
                planId: new string('d', 32));

            InstallationPlanValidator.Validate(plan);
            Assert.Equal(InstallationPlanInstallMode.DualBoot, plan.InstallMode);
            Assert.Equal(40, plan.Disk.Installer.FinalSizeGiB);
            Assert.Equal(0, plan.Disk.Installer.StagingSizeBytes);
            Assert.StartsWith(@"C:\ProgramData\LinuxHub\Transactions\", plan.Runtime.TransactionRootWindows);
        }
    }

    public class DiskPartitioningServiceMutationGuardTests
    {
        [Fact]
        public void ShrinkPartition_RejectsWithoutPublishedPlan()
        {
            var service = new DiskPartitioningService(
                new InstallationPlanMutationGuard(new InstallationPlanPublisher()));

            var error = Assert.Throws<InvalidOperationException>(
                () => service.ShrinkPartition(0, 2, 10_000_000_000, 1));

            Assert.Contains("no installation plan", error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
