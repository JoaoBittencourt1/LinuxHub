using System.IO;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Compensates started compensatable steps in reverse order, proving ownership before
    /// every destructive action (D4/D6). Gated by <see cref="InstallationSafetySwitches"/>.
    /// </summary>
    public sealed class CompensationOrchestrator : ICompensationOrchestrator
    {
        private readonly ICompensationDiskActions _disk;
        private readonly IMbrBackupService _mbrBackup;
        private readonly Func<bool> _isArmed;

        public CompensationOrchestrator(
            ICompensationDiskActions disk,
            IMbrBackupService mbrBackup,
            Func<bool>? isArmed = null)
        {
            _disk = disk ?? throw new ArgumentNullException(nameof(disk));
            _mbrBackup = mbrBackup ?? throw new ArgumentNullException(nameof(mbrBackup));
            _isArmed = isArmed ?? (() => InstallationSafetySwitches.RecoveryAndCompensationArmed);
        }

        public bool IsArmed => _isArmed();

        public CompensationResult Compensate(InstallationPlan plan, IInstallationExecutionLedger ledger)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(ledger);

            if (!_isArmed())
            {
                return new CompensationResult(
                    CompensationOutcomeKind.Disarmed,
                    [],
                    ["Compensation path is disarmed until VM validation (constitution §7.1)."],
                    EncryptionDivergence: null);
            }

            if (!string.Equals(
                    ledger.State.Status,
                    InstallationStatus.RollbackRunning,
                    StringComparison.Ordinal))
            {
                ledger.BeginRollback();
            }

            ObservedDiskIdentity observedDisk = _disk.ObserveDisk(plan.Disk.Number);
            DiskOwnershipProofResult diskProof = DiskOwnershipProof.ProveDisk(plan.Disk, observedDisk);
            if (diskProof.Status != DiskOwnershipProofStatus.Proven)
            {
                ledger.MarkRollbackIncomplete("ROLLBACK_TARGET_ABSENT", diskProof.Detail);
                return new CompensationResult(
                    CompensationOutcomeKind.TargetAbsent,
                    [.. ledger.State.CompensatedSteps],
                    [diskProof.Detail],
                    EncryptionStateComparison.Compare(plan.Runtime, _disk.ObserveEncryption()));
            }

            var details = new List<string>();
            bool aborted = false;

            foreach (string stepId in ledger.GetCompensationCandidates())
            {
                try
                {
                    CompensateStep(plan, stepId, details);
                    ledger.CompleteCompensation(stepId);
                }
                catch (CompensationAbortedException ex)
                {
                    details.Add(ex.Message);
                    aborted = true;
                    break;
                }
            }

            EncryptionDivergence? encryption =
                EncryptionStateComparison.Compare(plan.Runtime, _disk.ObserveEncryption());

            var observation = new RollbackVerificationObservation(
                StagingPartitionAbsent: _disk.StagingPartitionAbsent(plan),
                TransactionBootEntriesAbsent: _disk.TransactionBootEntriesAbsent(plan),
                WindowsGeometryRestored: _disk.WindowsGeometryMatchesPlan(plan),
                RecoveryGeometryRestored: _disk.RecoveryGeometryMatchesPlan(plan),
                BootStateRestored: _disk.BootStateRestored(plan),
                UncompensatedCompensatableSteps:
                    VerifiedRollbackPredicate.ListUncompensatedCompensatableSteps(ledger.State));

            RollbackVerificationResult verification = VerifiedRollbackPredicate.Evaluate(observation);
            // Log-copy failure must not downgrade a verified result (D7 / task 5.5).
            bool logCopyFailed = false;

            if (verification.Outcome == RollbackVerificationOutcome.Verified && !aborted)
            {
                ledger.CompleteRollback();
                return new CompensationResult(
                    CompensationOutcomeKind.Verified,
                    [.. ledger.State.CompensatedSteps],
                    details,
                    encryption,
                    logCopyFailed);
            }

            details.AddRange(verification.Failures);
            ledger.MarkRollbackIncomplete(
                "ROLLBACK_INCOMPLETE",
                string.Join("; ", details.Count > 0 ? details : verification.Failures));

            return new CompensationResult(
                CompensationOutcomeKind.Incomplete,
                [.. ledger.State.CompensatedSteps],
                details,
                encryption,
                logCopyFailed);
        }

        private void CompensateStep(InstallationPlan plan, string stepId, List<string> details)
        {
            switch (stepId)
            {
                case InstallationStepIds.WindowsTemporaryBootPrepared:
                    CompensateTemporaryBoot(plan, details);
                    break;
                case InstallationStepIds.WindowsInstallerConfigWritten:
                    details.Add("Installer config compensation is a best-effort file cleanup.");
                    break;
                case InstallationStepIds.WindowsStagingPrepared:
                    CompensateStaging(plan, details);
                    break;
                case InstallationStepIds.WindowsDiskPrepared:
                    CompensateDiskPrepared(plan, details);
                    break;
                default:
                    throw new CompensationAbortedException(
                        $"No compensation action is registered for step '{stepId}'.");
            }
        }

        private void CompensateTemporaryBoot(InstallationPlan plan, List<string> details)
        {
            if (string.Equals(plan.Firmware, InstallationPlanFirmware.Bios, StringComparison.OrdinalIgnoreCase))
            {
                string backupPath = Path.Combine(
                    plan.Runtime.TransactionRootWindows,
                    "mbr-backup.bin");
                if (!File.Exists(backupPath))
                {
                    throw new CompensationAbortedException(
                        $"MBR backup is missing at '{backupPath}'.");
                }

                // Ownership of the disk was already proven; RestoreMbr is behind IMbrBackupService.
                _mbrBackup.RestoreMbr(plan.Disk.Number, backupPath);
                details.Add("Restored MBR from transaction backup.");
            }

            _disk.RemoveTransactionBootEntries(plan);
            details.Add("Removed transaction boot entries.");
        }

        private void CompensateStaging(InstallationPlan plan, List<string> details)
        {
            if (plan.Disk.Installer.OffsetBytes is null or <= 0 ||
                plan.Disk.Installer.StagingSizeBytes <= 0)
            {
                details.Add("Staging was never recorded in the plan; nothing to remove.");
                return;
            }

            long offset = plan.Disk.Installer.OffsetBytes.Value;
            long size = plan.Disk.Installer.StagingSizeBytes;
            ObservedPartitionGeometry? observed = _disk.FindPartitionByOffset(plan.Disk.Number, offset);
            DiskOwnershipProofResult proof = DiskOwnershipProof.ProveStagingPartition(
                plan.Disk.Installer, observed);

            if (proof.Status == DiskOwnershipProofStatus.PartitionMissing)
            {
                details.Add("Staging partition already absent.");
                return;
            }

            if (proof.Status != DiskOwnershipProofStatus.Proven)
                throw new CompensationAbortedException(proof.Detail);

            _disk.RemovePartitionAtGeometry(plan.Disk.Number, offset, size);
            details.Add("Removed staging partition after ownership proof.");
        }

        private void CompensateDiskPrepared(InstallationPlan plan, List<string> details)
        {
            ObservedPartitionGeometry? windows = _disk.FindPartitionByOffset(
                plan.Disk.Number, plan.Disk.Windows.OffsetBytes);
            DiskOwnershipProofResult proof = DiskOwnershipProof.ProvePartitionAtOffset(
                plan.Disk.Windows.OffsetBytes, windows);

            if (proof.Status != DiskOwnershipProofStatus.Proven)
                throw new CompensationAbortedException(proof.Detail);

            if (windows!.SizeBytes == plan.Disk.Windows.SizeBytes)
            {
                details.Add("Windows partition already at planned size.");
                return;
            }

            _disk.RestoreWindowsPartitionSize(
                plan.Disk.Number,
                plan.Disk.Windows.Number,
                plan.Disk.Windows.SizeBytes);
            details.Add("Restored Windows partition size to plan geometry.");
        }
    }

    public sealed class CompensationAbortedException : Exception
    {
        public CompensationAbortedException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Production composition root wires this until phase 8. Guarantees the compensation
    /// path is unreachable at runtime (task 5.9).
    /// </summary>
    public sealed class DisarmedCompensationOrchestrator : ICompensationOrchestrator
    {
        public bool IsArmed => false;

        public CompensationResult Compensate(InstallationPlan plan, IInstallationExecutionLedger ledger)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(ledger);

            return new CompensationResult(
                CompensationOutcomeKind.Disarmed,
                [],
                [
                    "Compensation is disarmed until VM validation passes " +
                    "(InstallationSafetySwitches.RecoveryAndCompensationArmed, constitution §7.1).",
                ],
                EncryptionDivergence: null);
        }
    }
}
