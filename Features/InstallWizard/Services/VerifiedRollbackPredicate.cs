using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Post-compensation observation used by <see cref="VerifiedRollbackPredicate"/> (D7).
    /// Observation only — never used as progress authority (installation-state spec).
    /// </summary>
    public sealed record RollbackVerificationObservation(
        bool StagingPartitionAbsent,
        bool TransactionBootEntriesAbsent,
        bool WindowsGeometryRestored,
        bool RecoveryGeometryRestored,
        bool BootStateRestored,
        IReadOnlyList<string> UncompensatedCompensatableSteps);

    public enum RollbackVerificationOutcome
    {
        Verified,
        Incomplete,
    }

    public sealed record RollbackVerificationResult(
        RollbackVerificationOutcome Outcome,
        IReadOnlyList<string> Failures);

    /// <summary>
    /// Pure verified-rollback predicate (D7 / task 5.5). Ending compensation without an
    /// exception is not success — the five conditions are evaluated after acting.
    /// </summary>
    public static class VerifiedRollbackPredicate
    {
        public static RollbackVerificationResult Evaluate(RollbackVerificationObservation observation)
        {
            ArgumentNullException.ThrowIfNull(observation);

            var failures = new List<string>();

            if (observation.UncompensatedCompensatableSteps.Count > 0)
            {
                failures.Add(
                    "Uncompensated compensatable steps: " +
                    string.Join(", ", observation.UncompensatedCompensatableSteps));
            }

            if (!observation.StagingPartitionAbsent)
                failures.Add("Staging partition is still present.");

            if (!observation.TransactionBootEntriesAbsent)
                failures.Add("Transaction boot entries are still present.");

            if (!observation.WindowsGeometryRestored)
                failures.Add("Windows partition geometry was not restored to the plan values.");

            if (!observation.RecoveryGeometryRestored)
                failures.Add("Recovery partition geometry was not restored to the plan values.");

            if (!observation.BootStateRestored)
                failures.Add("Firmware or boot-code state was not restored.");

            return failures.Count == 0
                ? new RollbackVerificationResult(RollbackVerificationOutcome.Verified, failures)
                : new RollbackVerificationResult(RollbackVerificationOutcome.Incomplete, failures);
        }

        /// <summary>
        /// Builds the uncompensated list from durable state + catalog (task 5.5 condition 1).
        /// </summary>
        public static IReadOnlyList<string> ListUncompensatedCompensatableSteps(
            InstallationExecutionState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            var pending = new List<string>();
            foreach (InstallationStepDefinition step in InstallationStepCatalog.All)
            {
                if (!step.Compensatable)
                    continue;

                bool started =
                    state.CompletedSteps.Contains(step.Id, StringComparer.Ordinal) ||
                    string.Equals(state.ActiveStep, step.Id, StringComparison.Ordinal);

                if (!started)
                    continue;

                if (!state.CompensatedSteps.Contains(step.Id, StringComparer.Ordinal))
                    pending.Add(step.Id);
            }

            return pending;
        }
    }
}
