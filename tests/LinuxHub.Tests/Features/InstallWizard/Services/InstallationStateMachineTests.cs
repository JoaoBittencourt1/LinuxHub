using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class InstallationStateMachineTests
    {
        private static readonly string PlanId = new string('e', 32);

        [Fact]
        public void StartStep_AcceptsLegalTransition()
        {
            var machine = InstallationStateMachine.Create(PlanId);

            machine.StartStep(InstallationStepIds.WindowsPlanPublished);
            Assert.Equal(InstallationStepIds.WindowsPlanPublished, machine.State.ActiveStep);

            machine.CompleteStep(InstallationStepIds.WindowsPlanPublished);
            Assert.Null(machine.State.ActiveStep);
            Assert.Contains(InstallationStepIds.WindowsPlanPublished, machine.State.CompletedSteps);
        }

        [Fact]
        public void StartStep_RejectsOutOfOrder()
        {
            var machine = InstallationStateMachine.Create(PlanId);

            var error = Assert.Throws<InvalidOperationException>(
                () => machine.StartStep(InstallationStepIds.WindowsDiskPrepared));

            Assert.Contains("out of order", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void StartStep_RejectsDisarmedStep()
        {
            var machine = InstallationStateMachine.Create(PlanId);
            CompleteArmedWindowsPrefix(machine);

            var error = Assert.Throws<InvalidOperationException>(
                () => machine.StartStep(InstallationStepIds.LiveIsoMounted));

            Assert.Contains("disarmed", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MarkSucceeded_RejectsWhenRequiredArmedStepMissing()
        {
            var machine = InstallationStateMachine.Create(PlanId);
            machine.StartStep(InstallationStepIds.WindowsPlanPublished);
            machine.CompleteStep(InstallationStepIds.WindowsPlanPublished);

            var error = Assert.Throws<InvalidOperationException>(() => machine.MarkSucceeded());
            Assert.Contains(InstallationStepIds.WindowsDiskPrepared, error.Message);
        }

        [Fact]
        public void MarkSucceeded_IgnoresDisarmedRequiredSteps()
        {
            var machine = InstallationStateMachine.Create(PlanId);
            CompleteArmedWindowsPrefix(machine);
            machine.MarkSucceeded();

            Assert.Equal(InstallationStatus.Succeeded, machine.State.Status);
        }

        [Fact]
        public void Reentry_ReportsNextPendingWithoutReexecutingCompleted()
        {
            string directory = Path.Combine(Path.GetTempPath(), "linuxhub-state-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "installation-state.json");

            try
            {
                var ledger = InstallationExecutionLedger.Create(PlanId, path);
                ledger.StartStep(InstallationStepIds.WindowsPlanPublished);
                ledger.CompleteStep(InstallationStepIds.WindowsPlanPublished);
                ledger.StartStep(InstallationStepIds.WindowsDiskPrepared);
                ledger.CompleteStep(InstallationStepIds.WindowsDiskPrepared);

                var reopened = InstallationExecutionLedger.Open(path);
                Assert.Equal(
                    InstallationStepIds.WindowsStagingPrepared,
                    reopened.GetNextPendingArmedStepId());
                Assert.DoesNotContain(
                    InstallationStepIds.WindowsPlanPublished,
                    new[] { reopened.GetNextPendingArmedStepId() });
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void InterruptedWrite_DoesNotExposePartialState()
        {
            string directory = Path.Combine(Path.GetTempPath(), "linuxhub-state-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "installation-state.json");

            try
            {
                var ledger = InstallationExecutionLedger.Create(PlanId, path);
                ledger.StartStep(InstallationStepIds.WindowsPlanPublished);
                ledger.CompleteStep(InstallationStepIds.WindowsPlanPublished);
                string before = File.ReadAllText(path);

                AtomicJsonFile.Write(path, JsonSerializer.Serialize(ledger.State, InstallationExecutionLedger.SerializerOptions));
                Assert.True(File.ReadAllText(path).TrimEnd().EndsWith('}'));
                Assert.Contains("windows.plan-published", before, StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void SkipOptionalStep_AllowsDualBootToReachBootPrepared()
        {
            var machine = InstallationStateMachine.Create(PlanId);
            machine.StartStep(InstallationStepIds.WindowsPlanPublished);
            machine.CompleteStep(InstallationStepIds.WindowsPlanPublished);
            machine.StartStep(InstallationStepIds.WindowsDiskPrepared);
            machine.CompleteStep(InstallationStepIds.WindowsDiskPrepared);
            machine.SkipOptionalStep(InstallationStepIds.WindowsStagingPrepared);
            machine.SkipOptionalStep(InstallationStepIds.WindowsInstallerConfigWritten);
            machine.StartStep(InstallationStepIds.WindowsTemporaryBootPrepared);
            machine.CompleteStep(InstallationStepIds.WindowsTemporaryBootPrepared);
            machine.MarkSucceeded();

            Assert.Equal(InstallationStatus.Succeeded, machine.State.Status);
        }

        [Fact]
        public void ProgressCatalog_DoesNotAffectStateTransitions()
        {
            int percent = InstallationProgressCatalog.GetOverallPercent(
                InstallationStepIds.WindowsDiskPrepared);
            Assert.True(percent > 0);

            var machine = InstallationStateMachine.Create(PlanId);
            machine.StartStep(InstallationStepIds.WindowsPlanPublished);
            // Changing presentation percent must not be consulted by the state machine.
            Assert.Equal(InstallationStepIds.WindowsPlanPublished, machine.State.ActiveStep);
        }

        [Fact]
        public void SchemaParity_RequiredRootPropertiesSerialize()
        {
            string schemaPath = FindSchema("installation-state.schema.json");
            Assert.True(File.Exists(schemaPath), schemaPath);

            JsonNode root = JsonNode.Parse(File.ReadAllText(schemaPath))!;
            string[] required = root["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

            var state = InstallationStateMachine.Create(PlanId).State;
            string json = JsonSerializer.Serialize(state, InstallationExecutionLedger.SerializerOptions);
            JsonNode serialized = JsonNode.Parse(json)!;

            foreach (string property in required)
                Assert.True(
                    serialized.AsObject().ContainsKey(property),
                    $"Missing '{property}'");
        }

        private static void CompleteArmedWindowsPrefix(InstallationStateMachine machine)
        {
            machine.StartStep(InstallationStepIds.WindowsPlanPublished);
            machine.CompleteStep(InstallationStepIds.WindowsPlanPublished);
            machine.StartStep(InstallationStepIds.WindowsDiskPrepared);
            machine.CompleteStep(InstallationStepIds.WindowsDiskPrepared);
            machine.SkipOptionalStep(InstallationStepIds.WindowsStagingPrepared);
            machine.SkipOptionalStep(InstallationStepIds.WindowsInstallerConfigWritten);
            machine.StartStep(InstallationStepIds.WindowsTemporaryBootPrepared);
            machine.CompleteStep(InstallationStepIds.WindowsTemporaryBootPrepared);
        }

        private static string FindSchema(string fileName)
        {
            string candidate = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schemas", fileName));
            if (File.Exists(candidate))
                return candidate;

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "schemas", fileName));
        }
    }

    public class InstallationStepCatalogTests
    {
        [Fact]
        public void DisarmedSteps_ArePresentAndUnreachableThroughCatalogArmedFlag()
        {
            InstallationStepDefinition live = InstallationStepCatalog.Get(InstallationStepIds.LiveIsoMounted);
            Assert.False(live.Armed);
            Assert.True(live.Required);

            var machine = InstallationStateMachine.Create(new string('f', 32));
            Assert.Throws<InvalidOperationException>(
                () => machine.StartStep(InstallationStepIds.LiveIsoMounted));
        }

        [Fact]
        public void CatalogOrder_IsStable()
        {
            Assert.Equal(
                InstallationStepIds.WindowsPlanPublished,
                InstallationStepCatalog.All[0].Id);
            Assert.Equal(
                InstallationStepIds.TargetBootloaderInstalled,
                InstallationStepCatalog.All[^1].Id);
        }
    }
}
