using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Observed disk identity used by <see cref="DiskOwnershipProof"/> — supplied by a
    /// disk observer, never inferred from partition order.
    /// </summary>
    public sealed record ObservedDiskIdentity(
        int Number,
        string UniqueId,
        string PartitionTableId,
        long SizeBytes,
        string PartitionStyle);

    /// <summary>
    /// Observed partition geometry in exact bytes (D3).
    /// </summary>
    public sealed record ObservedPartitionGeometry(
        int Number,
        long OffsetBytes,
        long SizeBytes);

    public enum DiskOwnershipProofStatus
    {
        Proven,
        DiskMismatch,
        PartitionMismatch,
        PartitionMissing,
        IncompleteExpectation,
    }

    public sealed record DiskOwnershipProofResult(
        DiskOwnershipProofStatus Status,
        string Detail);

    /// <summary>
    /// Pure ownership proof (D6 / task 5.2). Separates "undo" from "destroy": every
    /// destructive compensation action must prove disk identity and exact partition
    /// geometry against the plan before touching anything.
    /// </summary>
    public static class DiskOwnershipProof
    {
        public static DiskOwnershipProofResult ProveDisk(
            InstallationPlanDisk expected,
            ObservedDiskIdentity observed)
        {
            ArgumentNullException.ThrowIfNull(expected);
            ArgumentNullException.ThrowIfNull(observed);

            if (string.IsNullOrWhiteSpace(expected.UniqueId) ||
                string.IsNullOrWhiteSpace(expected.PartitionTableId) ||
                expected.SizeBytes <= 0 ||
                string.IsNullOrWhiteSpace(expected.PartitionStyle))
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.IncompleteExpectation,
                    "Plan disk identity is incomplete.");
            }

            if (observed.Number != expected.Number)
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.DiskMismatch,
                    $"Disk number diverged: plan={expected.Number}, observed={observed.Number}.");
            }

            if (!string.Equals(observed.UniqueId.Trim(), expected.UniqueId.Trim(), StringComparison.Ordinal))
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.DiskMismatch,
                    "Disk UniqueId does not match the plan.");
            }

            if (!string.Equals(
                    observed.PartitionTableId.Trim(),
                    expected.PartitionTableId.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.DiskMismatch,
                    "Disk PartitionTableId does not match the plan.");
            }

            if (observed.SizeBytes != expected.SizeBytes)
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.DiskMismatch,
                    $"Disk size diverged: plan={expected.SizeBytes}, observed={observed.SizeBytes}.");
            }

            if (!string.Equals(
                    observed.PartitionStyle.Trim(),
                    expected.PartitionStyle.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.DiskMismatch,
                    "Disk PartitionStyle does not match the plan.");
            }

            return new DiskOwnershipProofResult(DiskOwnershipProofStatus.Proven, "Disk identity matches the plan.");
        }

        public static DiskOwnershipProofResult ProvePartition(
            InstallationPlanPartitionIdentity expected,
            ObservedPartitionGeometry? observed)
        {
            ArgumentNullException.ThrowIfNull(expected);

            if (expected.OffsetBytes <= 0 || expected.SizeBytes <= 0)
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.IncompleteExpectation,
                    "Plan partition geometry is incomplete.");
            }

            if (observed is null)
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.PartitionMissing,
                    $"No partition found at offset {expected.OffsetBytes}.");
            }

            if (observed.OffsetBytes != expected.OffsetBytes ||
                observed.SizeBytes != expected.SizeBytes)
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.PartitionMismatch,
                    $"Partition geometry diverged at offset {expected.OffsetBytes}: " +
                    $"plan size={expected.SizeBytes}, observed offset={observed.OffsetBytes} size={observed.SizeBytes}.");
            }

            return new DiskOwnershipProofResult(
                DiskOwnershipProofStatus.Proven,
                "Partition geometry matches the plan.");
        }

        /// <summary>
        /// Proves a partition exists at the planned offset on an already-proven disk.
        /// Used when restoring a shrunk Windows volume: size currently differs from the
        /// plan, so exact size proof would always fail before expand.
        /// </summary>
        public static DiskOwnershipProofResult ProvePartitionAtOffset(
            long expectedOffsetBytes,
            ObservedPartitionGeometry? observed)
        {
            if (expectedOffsetBytes <= 0)
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.IncompleteExpectation,
                    "Plan partition offset is incomplete.");
            }

            if (observed is null)
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.PartitionMissing,
                    $"No partition found at offset {expectedOffsetBytes}.");
            }

            if (observed.OffsetBytes != expectedOffsetBytes)
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.PartitionMismatch,
                    $"Partition offset diverged: plan={expectedOffsetBytes}, observed={observed.OffsetBytes}.");
            }

            return new DiskOwnershipProofResult(
                DiskOwnershipProofStatus.Proven,
                "Partition offset matches the plan.");
        }

        /// <summary>
        /// Staging identity may be absent until created — callers treat
        /// <see cref="DiskOwnershipProofStatus.IncompleteExpectation"/> as "nothing to undo"
        /// only when the plan never recorded a staging partition.
        /// </summary>
        public static DiskOwnershipProofResult ProveStagingPartition(
            InstallationPlanInstallerPartition expected,
            ObservedPartitionGeometry? observed)
        {
            ArgumentNullException.ThrowIfNull(expected);

            if (expected.OffsetBytes is null or <= 0 ||
                expected.StagingSizeBytes <= 0)
            {
                return new DiskOwnershipProofResult(
                    DiskOwnershipProofStatus.IncompleteExpectation,
                    "Plan has no staging partition geometry to prove.");
            }

            var identity = new InstallationPlanPartitionIdentity
            {
                Number = expected.Number ?? 0,
                OffsetBytes = expected.OffsetBytes.Value,
                SizeBytes = expected.StagingSizeBytes,
            };

            return ProvePartition(identity, observed);
        }
    }
}
