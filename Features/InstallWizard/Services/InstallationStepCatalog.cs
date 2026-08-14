namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// One entry in the ordered installation step catalog — the sole authority over
    /// progress (installation-state spec). Presentation labels live elsewhere.
    /// </summary>
    public sealed record InstallationStepDefinition(
        string Id,
        bool Required,
        bool Compensatable,
        bool Armed);

    /// <summary>
    /// Ordered catalog of installation steps. Disarmed live/target steps are reserved for
    /// the next change (D13.2) and cannot be started while <see cref="InstallationStepDefinition.Armed"/>
    /// is false.
    /// </summary>
    public static class InstallationStepCatalog
    {
        public static IReadOnlyList<InstallationStepDefinition> All { get; } =
        [
            new(Models.InstallationStepIds.WindowsPlanPublished, Required: true, Compensatable: false, Armed: true),
            new(Models.InstallationStepIds.WindowsDiskPrepared, Required: true, Compensatable: true, Armed: true),
            new(Models.InstallationStepIds.WindowsStagingPrepared, Required: false, Compensatable: true, Armed: true),
            new(Models.InstallationStepIds.WindowsInstallerConfigWritten, Required: false, Compensatable: true, Armed: true),
            new(Models.InstallationStepIds.WindowsTemporaryBootPrepared, Required: true, Compensatable: true, Armed: true),
            new(Models.InstallationStepIds.LiveIsoMounted, Required: true, Compensatable: true, Armed: false),
            new(Models.InstallationStepIds.LiveDistributionExtracted, Required: true, Compensatable: true, Armed: false),
            new(Models.InstallationStepIds.TargetSystemConfigured, Required: true, Compensatable: true, Armed: false),
            new(Models.InstallationStepIds.TargetBootloaderInstalled, Required: true, Compensatable: false, Armed: false),
        ];

        public static InstallationStepDefinition Get(string stepId) =>
            All.FirstOrDefault(s => string.Equals(s.Id, stepId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown installation step '{stepId}'.", nameof(stepId));

        public static int IndexOf(string stepId)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].Id, stepId, StringComparison.Ordinal))
                    return i;
            }

            throw new ArgumentException($"Unknown installation step '{stepId}'.", nameof(stepId));
        }

        public static string PhaseOf(string stepId)
        {
            int dot = stepId.IndexOf('.');
            if (dot <= 0)
                throw new ArgumentException($"Step '{stepId}' has no phase prefix.", nameof(stepId));
            return stepId[..dot];
        }
    }
}
