using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Resolves stable disk identity fields and partition snapshots for the plan from a
    /// <see cref="DiskLayout"/>. Pure — no I/O.
    /// </summary>
    public static class InstallationPlanDiskIdentity
    {
        public static string BuildPartitionTableId(DiskLayout layout)
        {
            ArgumentNullException.ThrowIfNull(layout);

            if (layout.IsGpt)
            {
                if (string.IsNullOrWhiteSpace(layout.Guid))
                {
                    throw new InvalidOperationException(
                        "GPT disk is missing a Guid; the installation plan cannot prove ownership.");
                }

                return "gpt:" + layout.Guid.ToLowerInvariant();
            }

            if (string.IsNullOrWhiteSpace(layout.DiskSignatureHex))
            {
                throw new InvalidOperationException(
                    "MBR disk is missing a Signature; the installation plan cannot prove ownership.");
            }

            return "mbr:" + layout.DiskSignatureHex.ToLowerInvariant();
        }

        public static string BuildUniqueId(DiskLayout layout)
        {
            ArgumentNullException.ThrowIfNull(layout);

            if (!string.IsNullOrWhiteSpace(layout.UniqueId))
                return layout.UniqueId;

            if (!string.IsNullOrWhiteSpace(layout.SerialNumber))
                return layout.SerialNumber;

            return BuildPartitionTableId(layout);
        }

        public static InstallationPlanPartitionIdentity ToIdentity(PartitionLayout partition) =>
            new()
            {
                Number = partition.Number,
                OffsetBytes = partition.OffsetBytes,
                SizeBytes = partition.SizeBytes,
            };

        public static InstallationPlanPartitionIdentity ResolveBootPartition(
            DiskLayout layout,
            bool isUefi)
        {
            ArgumentNullException.ThrowIfNull(layout);

            if (isUefi)
            {
                PartitionLayout esp = layout.EfiSystemPartition
                    ?? throw new InvalidOperationException(
                        "UEFI plan requires an EFI System Partition on the target disk.");
                return ToIdentity(esp);
            }

            PartitionLayout active = layout.ActiveMbrPartition
                ?? throw new InvalidOperationException(
                    "BIOS plan requires an active MBR partition on the target disk.");
            return ToIdentity(active);
        }

        public static InstallationPlanPartitionIdentity? ResolveRecoveryPartition(DiskLayout layout)
        {
            ArgumentNullException.ThrowIfNull(layout);
            return layout.WindowsRecoveryPartition is { } recovery
                ? ToIdentity(recovery)
                : null;
        }
    }
}
