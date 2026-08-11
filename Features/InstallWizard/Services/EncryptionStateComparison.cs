using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed record ObservedEncryptionState(
        string? ConversionStatus,
        double? PercentComplete);

    public sealed record EncryptionDivergence(
        string? PlannedConversionStatus,
        double? PlannedPercentComplete,
        string? ObservedConversionStatus,
        double? ObservedPercentComplete,
        string UserFacingDetailKey);

    /// <summary>
    /// Compares post-compensation encryption state to the plan capture (task 5.6).
    /// Divergence is reported outside the rollback verification result.
    /// </summary>
    public static class EncryptionStateComparison
    {
        public const string DivergenceResourceKey = "Wizard_RollbackEncryptionDivergence";

        public static EncryptionDivergence? Compare(
            InstallationPlanRuntime planned,
            ObservedEncryptionState observed)
        {
            ArgumentNullException.ThrowIfNull(planned);
            ArgumentNullException.ThrowIfNull(observed);

            string? plannedStatus = Normalize(planned.EncryptionConversionStatus);
            string? observedStatus = Normalize(observed.ConversionStatus);

            if (string.IsNullOrEmpty(plannedStatus))
                return null;

            bool statusMatches = string.Equals(plannedStatus, observedStatus, StringComparison.OrdinalIgnoreCase);
            bool percentMatches =
                planned.EncryptionPercentComplete is null && observed.PercentComplete is null ||
                planned.EncryptionPercentComplete is double plannedPct &&
                observed.PercentComplete is double observedPct &&
                Math.Abs(plannedPct - observedPct) < 0.01;

            if (statusMatches && percentMatches)
                return null;

            return new EncryptionDivergence(
                planned.EncryptionConversionStatus,
                planned.EncryptionPercentComplete,
                observed.ConversionStatus,
                observed.PercentComplete,
                DivergenceResourceKey);
        }

        private static string? Normalize(string? status) =>
            string.IsNullOrWhiteSpace(status) ? null : status.Trim();
    }
}
