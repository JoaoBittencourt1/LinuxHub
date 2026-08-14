using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Destructive compensation primitives. Each method is only invoked after
    /// <see cref="DiskOwnershipProof"/> succeeds for its target.
    /// </summary>
    public interface ICompensationDiskActions
    {
        ObservedDiskIdentity ObserveDisk(int diskNumber);

        ObservedPartitionGeometry? FindPartitionByOffset(int diskNumber, long offsetBytes);

        void RemovePartitionAtGeometry(int diskNumber, long offsetBytes, long sizeBytes);

        void RestoreWindowsPartitionSize(int diskNumber, int partitionNumber, long sizeBytes);

        void RestoreMbr(int diskNumber, string backupPath);

        void RemoveTransactionBootEntries(InstallationPlan plan);

        bool StagingPartitionAbsent(InstallationPlan plan);

        bool TransactionBootEntriesAbsent(InstallationPlan plan);

        bool WindowsGeometryMatchesPlan(InstallationPlan plan);

        bool RecoveryGeometryMatchesPlan(InstallationPlan plan);

        bool BootStateRestored(InstallationPlan plan);

        ObservedEncryptionState ObserveEncryption();
    }

    public enum CompensationOutcomeKind
    {
        Verified,
        Incomplete,
        Disarmed,
        TargetAbsent,
    }

    public sealed record CompensationResult(
        CompensationOutcomeKind Outcome,
        IReadOnlyList<string> CompensatedSteps,
        IReadOnlyList<string> Details,
        EncryptionDivergence? EncryptionDivergence,
        bool DiagnosticLogCopyFailed = false);

    public interface ICompensationOrchestrator
    {
        bool IsArmed { get; }

        CompensationResult Compensate(InstallationPlan plan, IInstallationExecutionLedger ledger);
    }
}
