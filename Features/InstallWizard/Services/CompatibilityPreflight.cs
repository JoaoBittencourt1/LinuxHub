namespace LinuxHub.Features.InstallWizard.Services
{
    public enum CompatibilitySeverity
    {
        Pass,
        Warning,
        Reject,
    }

    /// <summary>
    /// Machine facts gathered by the versioned preflight script (codes only — no prose).
    /// </summary>
    public sealed class CompatibilityFacts
    {
        public string? Firmware { get; init; }
        public string? DiskBusType { get; init; }
        public string? DiskPartitionStyle { get; init; }
        public bool? DiskIsDynamic { get; init; }
        public bool? DiskIsBoot { get; init; }
        public bool? DiskIsSystem { get; init; }
        public bool? IsStorageSpaces { get; init; }
        public bool? IsVirtualDisk { get; init; }
        public bool? IsIscsi { get; init; }
        public bool? RaidOrVmdSuspected { get; init; }
        public bool? TopologyDeterminate { get; init; }
        public string? EncryptionConversionStatus { get; init; }
        public double? EncryptionPercentComplete { get; init; }
        public int? EncryptionProtectionStatus { get; init; }
        public bool? EncryptionQuerySucceeded { get; init; }
        public string? BootNextProbeResult { get; init; }
        public long? ShrinkableBytes { get; init; }
        public long? RecoveryOffsetBytes { get; init; }
        public long? RecoverySizeBytes { get; init; }
    }

    public sealed record CompatibilityFinding(
        CompatibilitySeverity Severity,
        string Code,
        string MessageResourceKey);

    public sealed record CompatibilityPreflightReport(
        IReadOnlyList<CompatibilityFinding> Findings)
    {
        public bool HasRejection =>
            Findings.Any(f => f.Severity == CompatibilitySeverity.Reject);

        public IReadOnlyList<CompatibilityFinding> Rejections =>
            Findings.Where(f => f.Severity == CompatibilitySeverity.Reject).ToList();

        public IReadOnlyList<CompatibilityFinding> Warnings =>
            Findings.Where(f => f.Severity == CompatibilitySeverity.Warning).ToList();
    }

    /// <summary>
    /// One isolatable refusal/warning rule (task 6.11). Composed by the runner; never a
    /// monolithic "run everything" function.
    /// </summary>
    public interface ICompatibilityRule
    {
        CompatibilityFinding? Evaluate(CompatibilityFacts facts);
    }

    public interface ICompatibilityPreflightRunner
    {
        CompatibilityPreflightReport Evaluate(CompatibilityFacts facts);
    }

    public sealed class CompatibilityPreflightRunner : ICompatibilityPreflightRunner
    {
        private readonly IReadOnlyList<ICompatibilityRule> _rules;

        public CompatibilityPreflightRunner(IEnumerable<ICompatibilityRule> rules)
        {
            _rules = rules?.ToList() ?? throw new ArgumentNullException(nameof(rules));
        }

        public static CompatibilityPreflightRunner CreateDefault() =>
            new(
            [
                new DynamicDiskRule(),
                new StorageSpacesRule(),
                new UsbSystemDiskRule(),
                new VirtualOrIscsiDiskRule(),
                new RaidOrVmdRule(),
                new IndeterminateTopologyRule(),
                new EncryptionStateRule(),
                new BootNextProbeRule(),
            ]);

        public CompatibilityPreflightReport Evaluate(CompatibilityFacts facts)
        {
            ArgumentNullException.ThrowIfNull(facts);
            var findings = new List<CompatibilityFinding>();
            foreach (ICompatibilityRule rule in _rules)
            {
                CompatibilityFinding? finding = rule.Evaluate(facts);
                if (finding is not null)
                    findings.Add(finding);
            }

            return new CompatibilityPreflightReport(findings);
        }
    }
}
