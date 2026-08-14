using System.IO;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class CompensationOrchestratorTests
    {
        private static readonly string PlanId = new string('a', 32);

        [Fact]
        public void DisarmedOrchestrator_NeverTouchesDisk()
        {
            var disk = new FakeCompensationDisk();
            var orchestrator = new DisarmedCompensationOrchestrator();
            var ledger = InstallationExecutionLedger.Create(PlanId, TempStatePath());

            CompensationResult result = orchestrator.Compensate(SamplePlan(), ledger);

            Assert.Equal(CompensationOutcomeKind.Disarmed, result.Outcome);
            Assert.Empty(disk.RemovedGeometries);
            Assert.False(orchestrator.IsArmed);
        }

        [Fact]
        public void ProductionSafetySwitch_IsOff()
        {
            Assert.False(InstallationSafetySwitches.RecoveryAndCompensationArmed);
        }

        [Fact]
        public void DefaultArmedGate_RefusesEvenWhenConstructed()
        {
            var orchestrator = new CompensationOrchestrator(new FakeCompensationDisk(), new FakeMbr());
            CompensationResult result = orchestrator.Compensate(
                SamplePlan(),
                InstallationExecutionLedger.Create(PlanId, TempStatePath()));

            Assert.Equal(CompensationOutcomeKind.Disarmed, result.Outcome);
            Assert.False(orchestrator.IsArmed);
        }

        [Fact]
        public void ProvenStaging_IsRemoved()
        {
            InstallationPlan plan = SamplePlan();
            var disk = new FakeCompensationDisk
            {
                Disk = MatchDisk(plan),
                Partitions =
                [
                    new ObservedPartitionGeometry(
                        plan.Disk.Windows.Number,
                        plan.Disk.Windows.OffsetBytes,
                        plan.Disk.Windows.SizeBytes / 2),
                    new ObservedPartitionGeometry(
                        plan.Disk.Installer.Number!.Value,
                        plan.Disk.Installer.OffsetBytes!.Value,
                        plan.Disk.Installer.StagingSizeBytes),
                ],
            };
            disk.AfterRemoveRestoreWindowsToPlan = true;

            var ledger = SeedLedgerWithStagingAndBoot(plan);
            var orchestrator = new CompensationOrchestrator(disk, new FakeMbr(), isArmed: () => true);

            CompensationResult result = orchestrator.Compensate(plan, ledger);

            Assert.Equal(CompensationOutcomeKind.Verified, result.Outcome);
            Assert.Contains(
                disk.RemovedGeometries,
                g => g.Offset == plan.Disk.Installer.OffsetBytes && g.Size == plan.Disk.Installer.StagingSizeBytes);
            Assert.Contains(InstallationStepIds.WindowsStagingPrepared, result.CompensatedSteps);
        }

        [Fact]
        public void DivergentStagingGeometry_AbortsWithoutRemoval()
        {
            InstallationPlan plan = SamplePlan();
            var disk = new FakeCompensationDisk
            {
                Disk = MatchDisk(plan),
                Partitions =
                [
                    new ObservedPartitionGeometry(
                        plan.Disk.Installer.Number!.Value,
                        plan.Disk.Installer.OffsetBytes!.Value,
                        plan.Disk.Installer.StagingSizeBytes + 4096),
                ],
            };

            var ledger = SeedLedgerWithStagingAndBoot(plan);
            var orchestrator = new CompensationOrchestrator(disk, new FakeMbr(), isArmed: () => true);

            CompensationResult result = orchestrator.Compensate(plan, ledger);

            Assert.Equal(CompensationOutcomeKind.Incomplete, result.Outcome);
            Assert.Empty(disk.RemovedGeometries);
        }

        [Fact]
        public void DivergentDisk_CompensatesNothing()
        {
            InstallationPlan plan = SamplePlan();
            var disk = new FakeCompensationDisk
            {
                Disk = MatchDisk(plan) with { UniqueId = "wrong-disk" },
            };

            var ledger = SeedLedgerWithStagingAndBoot(plan);
            var orchestrator = new CompensationOrchestrator(disk, new FakeMbr(), isArmed: () => true);

            CompensationResult result = orchestrator.Compensate(plan, ledger);

            Assert.Equal(CompensationOutcomeKind.TargetAbsent, result.Outcome);
            Assert.Empty(disk.RemovedGeometries);
            Assert.Empty(result.CompensatedSteps);
        }

        [Fact]
        public void NeverStartedStep_IsNotCompensated()
        {
            InstallationPlan plan = SamplePlan();
            var disk = new FakeCompensationDisk { Disk = MatchDisk(plan), Partitions = [] };
            disk.MarkEverythingRestored = true;

            var ledger = InstallationExecutionLedger.Create(PlanId, TempStatePath());
            ledger.StartStep(InstallationStepIds.WindowsPlanPublished);
            ledger.CompleteStep(InstallationStepIds.WindowsPlanPublished);
            ledger.Fail("test", "stopped early", InstallationPhase.Windows);

            var mbr = new FakeMbr();
            var orchestrator = new CompensationOrchestrator(disk, mbr, isArmed: () => true);

            CompensationResult result = orchestrator.Compensate(plan, ledger);

            Assert.DoesNotContain(InstallationStepIds.WindowsTemporaryBootPrepared, result.CompensatedSteps);
            Assert.False(mbr.RestoreCalled);
            Assert.Equal(CompensationOutcomeKind.Verified, result.Outcome);
        }

        [Fact]
        public void VerificationFails_WhenWindowsGeometryNotRestored()
        {
            InstallationPlan plan = SamplePlan();
            var disk = new FakeCompensationDisk
            {
                Disk = MatchDisk(plan),
                Partitions =
                [
                    new ObservedPartitionGeometry(
                        plan.Disk.Windows.Number,
                        plan.Disk.Windows.OffsetBytes,
                        plan.Disk.Windows.SizeBytes / 2),
                ],
                RefuseWindowsRestore = true,
            };

            var ledger = InstallationExecutionLedger.Create(PlanId, TempStatePath());
            ledger.StartStep(InstallationStepIds.WindowsPlanPublished);
            ledger.CompleteStep(InstallationStepIds.WindowsPlanPublished);
            ledger.StartStep(InstallationStepIds.WindowsDiskPrepared);
            ledger.CompleteStep(InstallationStepIds.WindowsDiskPrepared);
            ledger.Fail("test", "interrupted", InstallationPhase.Windows);

            var orchestrator = new CompensationOrchestrator(disk, new FakeMbr(), isArmed: () => true);
            CompensationResult result = orchestrator.Compensate(plan, ledger);

            Assert.Equal(CompensationOutcomeKind.Incomplete, result.Outcome);
            Assert.Contains(result.Details, d => d.Contains("Windows", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void EncryptionDivergence_IsReportedOutsideVerifiedOutcome()
        {
            InstallationPlan plan = SamplePlan();
            plan.Runtime.EncryptionConversionStatus = "FullyEncrypted";
            plan.Runtime.EncryptionPercentComplete = 100;

            var disk = new FakeCompensationDisk
            {
                Disk = MatchDisk(plan),
                Partitions = [],
                MarkEverythingRestored = true,
                Encryption = new ObservedEncryptionState("FullyDecrypted", 0),
            };

            var ledger = InstallationExecutionLedger.Create(PlanId, TempStatePath());
            ledger.StartStep(InstallationStepIds.WindowsPlanPublished);
            ledger.CompleteStep(InstallationStepIds.WindowsPlanPublished);
            ledger.Fail("test", "x", InstallationPhase.Windows);

            var orchestrator = new CompensationOrchestrator(disk, new FakeMbr(), isArmed: () => true);
            CompensationResult result = orchestrator.Compensate(plan, ledger);

            Assert.Equal(CompensationOutcomeKind.Verified, result.Outcome);
            Assert.NotNull(result.EncryptionDivergence);
            Assert.Equal(
                EncryptionStateComparison.DivergenceResourceKey,
                result.EncryptionDivergence!.UserFacingDetailKey);
        }

        [Fact]
        public void FlowRunner_DoesNotReferenceCompensationOrchestrator()
        {
            var ctor = typeof(InstallationFlowRunner).GetConstructors().Single();
            Assert.DoesNotContain(
                ctor.GetParameters(),
                p => p.ParameterType == typeof(ICompensationOrchestrator) ||
                     p.ParameterType == typeof(IRecoveryAgentRegistrar));
        }

        [Fact]
        public void StartedButIncompleteStep_IsCompensationCandidate()
        {
            var machine = InstallationStateMachine.Create(PlanId);
            machine.StartStep(InstallationStepIds.WindowsPlanPublished);
            machine.CompleteStep(InstallationStepIds.WindowsPlanPublished);
            machine.StartStep(InstallationStepIds.WindowsDiskPrepared);
            machine.Fail("x", "mid-step", InstallationPhase.Windows);

            Assert.Equal(InstallationStepIds.WindowsDiskPrepared, machine.State.ActiveStep);
            Assert.Contains(InstallationStepIds.WindowsDiskPrepared, machine.GetCompensationCandidates());
        }

        [Fact]
        public void RecoveryAgentRegistrar_DefaultIsDisarmed()
        {
            var registrar = new RecoveryAgentRegistrar(
                runRegisterScript: (_, _) => "ok",
                runUnregisterScript: _ => "ok",
                taskExists: _ => true);

            Assert.False(registrar.IsArmed);
            Assert.Throws<InvalidOperationException>(() => registrar.Register(PlanId, @"C:\t"));
        }

        [Fact]
        public void VersionedScripts_ExistUnderScriptsRoot()
        {
            Assert.True(File.Exists(ScriptCatalog.GetPath(ScriptCatalog.RestoreMbr)));
            Assert.True(File.Exists(ScriptCatalog.GetPath(ScriptCatalog.RemovePartitionByGeometry)));
            Assert.True(File.Exists(ScriptCatalog.GetPath(ScriptCatalog.RegisterRecoveryTask)));
            Assert.True(File.Exists(ScriptCatalog.GetPath(ScriptCatalog.UnregisterRecoveryTask)));
            Assert.True(File.Exists(ScriptCatalog.GetPath(ScriptCatalog.RecoveryAgent)));

            string agent = ScriptCatalog.Read(ScriptCatalog.RecoveryAgent);
            Assert.Contains("DISARMED", agent, StringComparison.Ordinal);
        }

        private static IInstallationExecutionLedger SeedLedgerWithStagingAndBoot(InstallationPlan plan)
        {
            var ledger = InstallationExecutionLedger.Create(plan.PlanId, TempStatePath());
            ledger.StartStep(InstallationStepIds.WindowsPlanPublished);
            ledger.CompleteStep(InstallationStepIds.WindowsPlanPublished);
            ledger.StartStep(InstallationStepIds.WindowsDiskPrepared);
            ledger.CompleteStep(InstallationStepIds.WindowsDiskPrepared);
            ledger.StartStep(InstallationStepIds.WindowsStagingPrepared);
            ledger.CompleteStep(InstallationStepIds.WindowsStagingPrepared);
            ledger.SkipOptionalStep(InstallationStepIds.WindowsInstallerConfigWritten);
            ledger.StartStep(InstallationStepIds.WindowsTemporaryBootPrepared);
            ledger.CompleteStep(InstallationStepIds.WindowsTemporaryBootPrepared);
            ledger.Fail("test", "reboot failed", InstallationPhase.Windows);
            return ledger;
        }

        private static string TempStatePath()
        {
            string dir = Path.Combine(Path.GetTempPath(), "linuxhub-comp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "installation-state.json");
        }

        private static ObservedDiskIdentity MatchDisk(InstallationPlan plan) =>
            new(
                plan.Disk.Number,
                plan.Disk.UniqueId,
                plan.Disk.PartitionTableId,
                plan.Disk.SizeBytes,
                plan.Disk.PartitionStyle);

        private static InstallationPlan SamplePlan() => new()
        {
            PlanId = PlanId,
            Firmware = InstallationPlanFirmware.Uefi,
            InstallMode = InstallationPlanInstallMode.Replace,
            Disk = new InstallationPlanDisk
            {
                Number = 0,
                UniqueId = "disk-unique",
                PartitionTableId = "gpt:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                SizeBytes = 512L * 1024 * 1024 * 1024,
                PartitionStyle = InstallationPlanPartitionStyle.Gpt,
                SystemDrive = "C:",
                Windows = new InstallationPlanPartitionIdentity
                {
                    Number = 3,
                    OffsetBytes = 1_048_576,
                    SizeBytes = 200L * 1024 * 1024 * 1024,
                },
                Boot = new InstallationPlanPartitionIdentity
                {
                    Number = 1,
                    OffsetBytes = 1024 * 1024,
                    SizeBytes = 100 * 1024 * 1024,
                },
                Installer = new InstallationPlanInstallerPartition
                {
                    Number = 5,
                    OffsetBytes = 400L * 1024 * 1024 * 1024,
                    StagingSizeBytes = 8L * 1024 * 1024 * 1024,
                    FinalSizeGiB = 40,
                },
            },
            Runtime = new InstallationPlanRuntime
            {
                TransactionRootWindows = Path.Combine(Path.GetTempPath(), "linuxhub-tx-" + PlanId),
            },
        };

        private sealed class FakeMbr : IMbrBackupService
        {
            public bool RestoreCalled { get; private set; }
            public string BackupMbr(int diskIndex, string backupPath) => backupPath;
            public void WriteBootCode(int diskIndex, string bootCodeFilePath) { }
            public void RestoreMbr(int diskIndex, string backupPath) => RestoreCalled = true;
            public byte[] ReadMbr(int diskIndex) => new byte[512];
            public void WriteCoreImageToGap(int diskIndex, string coreImageFilePath) { }
        }

        private sealed class FakeCompensationDisk : ICompensationDiskActions
        {
            public ObservedDiskIdentity Disk { get; set; } = new(0, "x", "gpt:x", 1, "GPT");
            public List<ObservedPartitionGeometry> Partitions { get; set; } = [];
            public List<(long Offset, long Size)> RemovedGeometries { get; } = [];
            public bool MarkEverythingRestored { get; set; }
            public bool AfterRemoveRestoreWindowsToPlan { get; set; }
            public bool RefuseWindowsRestore { get; set; }
            public ObservedEncryptionState Encryption { get; set; } = new(null, null);
            private bool _windowsRestored;

            public ObservedDiskIdentity ObserveDisk(int diskNumber) => Disk;

            public ObservedPartitionGeometry? FindPartitionByOffset(int diskNumber, long offsetBytes) =>
                Partitions.FirstOrDefault(p => p.OffsetBytes == offsetBytes);

            public void RemovePartitionAtGeometry(int diskNumber, long offsetBytes, long sizeBytes)
            {
                RemovedGeometries.Add((offsetBytes, sizeBytes));
                Partitions.RemoveAll(p => p.OffsetBytes == offsetBytes && p.SizeBytes == sizeBytes);
            }

            public void RestoreWindowsPartitionSize(int diskNumber, int partitionNumber, long sizeBytes)
            {
                if (RefuseWindowsRestore)
                    return;

                _windowsRestored = true;
                for (int i = 0; i < Partitions.Count; i++)
                {
                    if (Partitions[i].Number == partitionNumber)
                    {
                        Partitions[i] = Partitions[i] with { SizeBytes = sizeBytes };
                        return;
                    }
                }
            }

            public void RestoreMbr(int diskNumber, string backupPath) { }
            public void RemoveTransactionBootEntries(InstallationPlan plan) { }

            public bool StagingPartitionAbsent(InstallationPlan plan) =>
                MarkEverythingRestored ||
                plan.Disk.Installer.OffsetBytes is null ||
                FindPartitionByOffset(plan.Disk.Number, plan.Disk.Installer.OffsetBytes.Value) is null;

            public bool TransactionBootEntriesAbsent(InstallationPlan plan) => true;

            public bool WindowsGeometryMatchesPlan(InstallationPlan plan) =>
                MarkEverythingRestored ||
                _windowsRestored ||
                AfterRemoveRestoreWindowsToPlan ||
                FindPartitionByOffset(plan.Disk.Number, plan.Disk.Windows.OffsetBytes) is { } w &&
                w.SizeBytes == plan.Disk.Windows.SizeBytes;

            public bool RecoveryGeometryMatchesPlan(InstallationPlan plan) => true;
            public bool BootStateRestored(InstallationPlan plan) => true;
            public ObservedEncryptionState ObserveEncryption() => Encryption;
        }
    }
}
