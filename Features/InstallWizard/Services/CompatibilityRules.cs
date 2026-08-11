namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed class DynamicDiskRule : ICompatibilityRule
    {
        public CompatibilityFinding? Evaluate(CompatibilityFacts facts) =>
            facts.DiskIsDynamic == true
                ? new CompatibilityFinding(
                    CompatibilitySeverity.Reject,
                    "COMPAT_E_DYNAMIC_DISK",
                    "Preflight_RejectDynamicDisk")
                : null;
    }

    public sealed class StorageSpacesRule : ICompatibilityRule
    {
        public CompatibilityFinding? Evaluate(CompatibilityFacts facts) =>
            facts.IsStorageSpaces == true
                ? new CompatibilityFinding(
                    CompatibilitySeverity.Reject,
                    "COMPAT_E_STORAGE_SPACES",
                    "Preflight_RejectStorageSpaces")
                : null;
    }

    public sealed class UsbSystemDiskRule : ICompatibilityRule
    {
        public CompatibilityFinding? Evaluate(CompatibilityFacts facts) =>
            facts.DiskIsSystem == true &&
            string.Equals(facts.DiskBusType, "USB", StringComparison.OrdinalIgnoreCase)
                ? new CompatibilityFinding(
                    CompatibilitySeverity.Reject,
                    "COMPAT_E_USB_SYSTEM_DISK",
                    "Preflight_RejectUsbSystemDisk")
                : null;
    }

    public sealed class VirtualOrIscsiDiskRule : ICompatibilityRule
    {
        public CompatibilityFinding? Evaluate(CompatibilityFacts facts)
        {
            if (facts.IsVirtualDisk == true)
            {
                return new CompatibilityFinding(
                    CompatibilitySeverity.Reject,
                    "COMPAT_E_VIRTUAL_DISK",
                    "Preflight_RejectVirtualDisk");
            }

            if (facts.IsIscsi == true)
            {
                return new CompatibilityFinding(
                    CompatibilitySeverity.Reject,
                    "COMPAT_E_ISCSI",
                    "Preflight_RejectIscsi");
            }

            return null;
        }
    }

    public sealed class RaidOrVmdRule : ICompatibilityRule
    {
        public CompatibilityFinding? Evaluate(CompatibilityFacts facts) =>
            facts.RaidOrVmdSuspected == true
                ? new CompatibilityFinding(
                    CompatibilitySeverity.Reject,
                    "COMPAT_E_RAID_VMD",
                    "Preflight_RejectRaidOrVmd")
                : null;
    }

    public sealed class IndeterminateTopologyRule : ICompatibilityRule
    {
        public CompatibilityFinding? Evaluate(CompatibilityFacts facts) =>
            facts.TopologyDeterminate == false
                ? new CompatibilityFinding(
                    CompatibilitySeverity.Reject,
                    "COMPAT_E_TOPOLOGY_INDETERMINATE",
                    "Preflight_RejectIndeterminateTopology")
                : null;
    }

    public sealed class EncryptionStateRule : ICompatibilityRule
    {
        public CompatibilityFinding? Evaluate(CompatibilityFacts facts)
        {
            if (facts.EncryptionQuerySucceeded == false)
            {
                return new CompatibilityFinding(
                    CompatibilitySeverity.Reject,
                    "COMPAT_E_ENCRYPTION_UNREADABLE",
                    "Preflight_RejectEncryptionUnreadable");
            }

            if (facts.EncryptionQuerySucceeded != true)
                return null;

            var state = new VolumeEncryptionState(
                facts.EncryptionConversionStatus ?? "Unknown",
                facts.EncryptionPercentComplete ?? 0,
                facts.EncryptionProtectionStatus ?? -1,
                QuerySucceeded: true);

            if (state.BlocksStagingBoot)
            {
                return new CompatibilityFinding(
                    CompatibilitySeverity.Reject,
                    "COMPAT_E_ENCRYPTION_ACTIVE",
                    "Preflight_RejectEncryptionActive");
            }

            return null;
        }
    }

    /// <summary>
    /// BootNext probe skipped is a warning only — never treated as approval (task 6.5).
    /// </summary>
    public sealed class BootNextProbeRule : ICompatibilityRule
    {
        public CompatibilityFinding? Evaluate(CompatibilityFacts facts)
        {
            if (string.Equals(facts.BootNextProbeResult, "skipped", StringComparison.OrdinalIgnoreCase))
            {
                return new CompatibilityFinding(
                    CompatibilitySeverity.Warning,
                    "COMPAT_W_BOOTNEXT_SKIPPED",
                    "Preflight_WarnBootNextSkipped");
            }

            if (string.Equals(facts.BootNextProbeResult, "failed", StringComparison.OrdinalIgnoreCase))
            {
                return new CompatibilityFinding(
                    CompatibilitySeverity.Warning,
                    "COMPAT_W_BOOTNEXT_FAILED",
                    "Preflight_WarnBootNextFailed");
            }

            return null;
        }
    }
}
