using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Resolves the Windows partition recorded in the plan from the live disk layout and
    /// the wizard selection. Pure — no I/O.
    /// </summary>
    public static class InstallationPlanWindowsPartition
    {
        private static readonly HashSet<string> ExcludedGptTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}", // EFI System
            "{e3c9e316-0b5c-4db8-817d-f92df00215ae}", // Microsoft Reserved
            "{de94bba4-06d1-4d40-a16a-bfd50179d6ac}", // Windows Recovery
        };

        public static InstallationPlanPartitionIdentity Resolve(
            DiskLayout layout,
            int? dualBootPartitionNumber)
        {
            ArgumentNullException.ThrowIfNull(layout);

            if (dualBootPartitionNumber is int number)
            {
                PartitionLayout selected = layout.Partitions.FirstOrDefault(p => p.Number == number)
                    ?? throw new InvalidOperationException(
                        $"Partition {number} was not found on disk {layout.Index}.");
                return InstallationPlanDiskIdentity.ToIdentity(selected);
            }

            PartitionLayout windows = layout.Partitions
                .Where(p => !ExcludedGptTypes.Contains(p.GptType))
                .OrderByDescending(p => p.SizeBytes)
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"No Windows data partition was found on disk {layout.Index}.");

            return InstallationPlanDiskIdentity.ToIdentity(windows);
        }
    }
}
