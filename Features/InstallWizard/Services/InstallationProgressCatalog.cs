using LinuxHub.Common.Localization;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Presentation-only catalog: labels and percentages for the UI. Has no authority over
    /// persisted state (installation-state spec) and no reference back to the ledger.
    /// </summary>
    public static class InstallationProgressCatalog
    {
        private static readonly IReadOnlyDictionary<string, ProgressPresentation> Entries =
            new Dictionary<string, ProgressPresentation>(StringComparer.Ordinal)
            {
                [InstallationStepIds.WindowsPlanPublished] = new("Wizard_InstallStepPublishingPlan", 5),
                [InstallationStepIds.WindowsDiskPrepared] = new("Wizard_InstallStepShrinking", 25),
                [InstallationStepIds.WindowsStagingPrepared] = new("Wizard_InstallStepCopyingIso", 55),
                [InstallationStepIds.WindowsInstallerConfigWritten] = new("Wizard_InstallStepWritingConfig", 75),
                [InstallationStepIds.WindowsTemporaryBootPrepared] = new("Wizard_InstallStepStagingBoot", 95),
            };

        public static string GetStatusText(string stepId, LocalizationManager loc)
        {
            ArgumentNullException.ThrowIfNull(loc);
            if (Entries.TryGetValue(stepId, out ProgressPresentation? presentation))
                return loc[presentation.StatusKey];

            return stepId;
        }

        public static int GetOverallPercent(string stepId) =>
            Entries.TryGetValue(stepId, out ProgressPresentation? presentation)
                ? presentation.OverallPercent
                : 0;

        private sealed record ProgressPresentation(string StatusKey, int OverallPercent);
    }
}
