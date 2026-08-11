using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class VerifiedRollbackPredicateTests
    {
        [Fact]
        public void Evaluate_VerifiedWhenAllConditionsHold()
        {
            var result = VerifiedRollbackPredicate.Evaluate(new RollbackVerificationObservation(
                StagingPartitionAbsent: true,
                TransactionBootEntriesAbsent: true,
                WindowsGeometryRestored: true,
                RecoveryGeometryRestored: true,
                BootStateRestored: true,
                UncompensatedCompensatableSteps: []));

            Assert.Equal(RollbackVerificationOutcome.Verified, result.Outcome);
            Assert.Empty(result.Failures);
        }

        [Fact]
        public void Evaluate_IncompleteWhenGeometryNotRestored()
        {
            var result = VerifiedRollbackPredicate.Evaluate(new RollbackVerificationObservation(
                StagingPartitionAbsent: true,
                TransactionBootEntriesAbsent: true,
                WindowsGeometryRestored: false,
                RecoveryGeometryRestored: true,
                BootStateRestored: true,
                UncompensatedCompensatableSteps: []));

            Assert.Equal(RollbackVerificationOutcome.Incomplete, result.Outcome);
            Assert.Contains(result.Failures, f => f.Contains("Windows", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ListUncompensated_IgnoresNeverStartedSteps()
        {
            var state = InstallationStateMachine.Create(new string('b', 32)).State;
            state.CompletedSteps.Add(InstallationStepIds.WindowsPlanPublished);

            Assert.Empty(VerifiedRollbackPredicate.ListUncompensatedCompensatableSteps(state));
        }
    }
}
