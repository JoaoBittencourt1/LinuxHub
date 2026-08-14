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

        /// <summary>
        /// own-linux-installer task 9.1: os quatro passos live/target que este teste travava
        /// como inalcançáveis agora estão Armed=true — é esta mudança que os liga. A cobertura
        /// do CÓDIGO de rejeição de passo desarmado (InstallationStateMachine.StartStep)
        /// continua existindo (nenhum passo do catálogo hoje está desarmado para exercitá-la
        /// de ponta a ponta com dado real; a asserção equivalente vive agora em
        /// InstallationStepCatalogParityTests, que prova que os cinco estão Armed).
        /// </summary>
        [Fact]
        public void StartStep_AllowsLiveAndTargetStepsNowThatTheyAreArmed()
        {
            var machine = InstallationStateMachine.Create(PlanId);
            CompleteArmedWindowsPrefix(machine);

            machine.StartStep(InstallationStepIds.LiveIsoMounted);
            Assert.Equal(InstallationStepIds.LiveIsoMounted, machine.State.ActiveStep);
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

        /// <summary>
        /// Bug real encontrado testando dual-boot desatendido do Ubuntu (Subiquity) em VM:
        /// MarkSucceeded passou a exigir os passos live/target (armados pela task 9.1) mesmo
        /// para mecanismos que nunca os iniciam — Subiquity, UbiquityPreseed, Archinstall,
        /// dual-boot manual, modo substituir terminam o que o Windows acompanha em
        /// windows.temporary-boot-prepared; o resto acontece dentro do instalador nativo da
        /// distro, depois do reboot, fora do nosso registro. Travava TODA instalação por esses
        /// mecanismos, não só a nova.
        ///
        /// MarkSucceeded só é chamado de <c>InstallationFlowRunner.Run</c>, e nunca para o
        /// mecanismo <c>OwnLiveInstaller</c> (esse pula a chamada — ver
        /// InstallationFlowRunnerTests/InstallationFlowRunner.cs); quem marca sucesso pra ele é
        /// o instalador live, escrevendo direto no registro espelhado depois do reboot. Por
        /// isso a checagem aqui é só da fase windows — os passos live/target continuam
        /// Required/Armed no catálogo (D12), só não bloqueiam ESTE método.
        /// </summary>
        [Fact]
        public void MarkSucceeded_OnlyRequiresTheWindowsPhase_LiveAndTargetStepsDontBlockIt()
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

            // O próximo passo armado pendente é live.iso-mounted (só o instalador live
            // completa, pós-reboot) — mas isso não impede MarkSucceeded para este mecanismo,
            // que termina o que acompanha aqui mesmo.
            Assert.Equal(InstallationStepIds.LiveIsoMounted, machine.GetNextPendingArmedStepId());

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

        /// <summary>
        /// own-linux-installer task 9.1/9.2: os cinco passos live/target, na ordem que o
        /// instalador live (bash, pós-reboot) os completa. Não passa por MarkSucceeded — esse
        /// mecanismo (OwnLiveInstaller) nunca chama esse método em C# (ver
        /// InstallationFlowRunner.Run); é o próprio bash quem escreve o estado terminal no
        /// registro espelhado, depois de completar esta sequência.
        /// </summary>
        [Fact]
        public void StartAndCompleteStep_WalksTheFullLiveAndTargetChainInOrder()
        {
            var machine = InstallationStateMachine.Create(PlanId);
            CompleteArmedWindowsPrefix(machine);

            machine.StartStep(InstallationStepIds.LiveIsoMounted);
            machine.CompleteStep(InstallationStepIds.LiveIsoMounted);
            machine.StartStep(InstallationStepIds.LiveDistributionExtracted);
            machine.CompleteStep(InstallationStepIds.LiveDistributionExtracted);
            machine.StartStep(InstallationStepIds.TargetBootloaderPackagesInstalled);
            machine.CompleteStep(InstallationStepIds.TargetBootloaderPackagesInstalled);
            machine.StartStep(InstallationStepIds.TargetSystemConfigured);
            machine.CompleteStep(InstallationStepIds.TargetSystemConfigured);
            machine.StartStep(InstallationStepIds.TargetBootloaderInstalled);
            machine.CompleteStep(InstallationStepIds.TargetBootloaderInstalled);
            machine.StartStep(InstallationStepIds.TargetInstallationVerified);
            machine.CompleteStep(InstallationStepIds.TargetInstallationVerified);

            Assert.Contains(InstallationStepIds.TargetInstallationVerified, machine.State.CompletedSteps);
            Assert.Null(machine.State.ActiveStep);
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
        /// <summary>
        /// own-linux-installer task 9.1/9.5 (design.md D0-D12): o teste que provava estes
        /// quatro passos inalcançáveis (D13.2 do change anterior) precisa provar o oposto
        /// agora — é esta mudança que os liga. Segue a mesma regra do tasks.md: "o teste que
        /// provava a inalcançabilidade dos quatro precisa passar a provar outra coisa, não ser
        /// apagado".
        /// </summary>
        [Fact]
        public void LiveAndTargetSteps_AreArmedAndReachableInOrder()
        {
            InstallationStepDefinition live = InstallationStepCatalog.Get(InstallationStepIds.LiveIsoMounted);
            Assert.True(live.Armed);
            Assert.True(live.Required);

            var machine = InstallationStateMachine.Create(new string('f', 32));
            machine.StartStep(InstallationStepIds.WindowsPlanPublished);
            machine.CompleteStep(InstallationStepIds.WindowsPlanPublished);
            machine.StartStep(InstallationStepIds.WindowsDiskPrepared);
            machine.CompleteStep(InstallationStepIds.WindowsDiskPrepared);
            machine.SkipOptionalStep(InstallationStepIds.WindowsStagingPrepared);
            machine.SkipOptionalStep(InstallationStepIds.WindowsInstallerConfigWritten);
            machine.StartStep(InstallationStepIds.WindowsTemporaryBootPrepared);
            machine.CompleteStep(InstallationStepIds.WindowsTemporaryBootPrepared);

            machine.StartStep(InstallationStepIds.LiveIsoMounted);
            Assert.Equal(InstallationStepIds.LiveIsoMounted, machine.State.ActiveStep);
        }

        [Fact]
        public void CatalogOrder_IsStable()
        {
            Assert.Equal(
                InstallationStepIds.WindowsPlanPublished,
                InstallationStepCatalog.All[0].Id);
            Assert.Equal(
                InstallationStepIds.TargetInstallationVerified,
                InstallationStepCatalog.All[^1].Id);
        }
    }
}
