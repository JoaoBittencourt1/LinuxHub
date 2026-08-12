using System.Text.RegularExpressions;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Applies legal state transitions. Pure regarding I/O — callers persist after each
    /// successful mutation.
    /// </summary>
    public sealed class InstallationStateMachine
    {
        private static readonly Regex StepPattern = new(
            "^(windows|live|target)\\.[a-z0-9]+(?:-[a-z0-9]+)*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ProgressStagePattern = new(
            "^[a-z0-9]+(?:-[a-z0-9]+)*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public InstallationStateMachine(InstallationExecutionState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            ValidateState(state);
        }

        public InstallationExecutionState State { get; }

        public static InstallationStateMachine Create(string planId)
        {
            if (!IsHexId(planId))
            {
                throw new ArgumentException(
                    "A 32-character lowercase hexadecimal planId is required.", nameof(planId));
            }

            return new InstallationStateMachine(new InstallationExecutionState
            {
                PlanId = planId,
                Status = InstallationStatus.Pending,
                Phase = InstallationPhase.Windows,
                Progress = new InstallationProgress
                {
                    Stage = "initializing",
                    OverallPercent = 0,
                },
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        public void SetProgress(string stage, int overallPercent, int? detailPercent = null)
        {
            if (string.IsNullOrWhiteSpace(stage) || !ProgressStagePattern.IsMatch(stage))
                throw new ArgumentException("A stable progress stage is required.", nameof(stage));
            if (overallPercent is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(overallPercent));
            if (detailPercent is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(detailPercent));

            State.Progress = new InstallationProgress
            {
                Stage = stage,
                OverallPercent = overallPercent,
                DetailPercent = detailPercent,
            };
            Touch();
        }

        public void StartStep(string step)
        {
            InstallationStepDefinition definition = InstallationStepCatalog.Get(step);
            if (!definition.Armed)
            {
                throw new InvalidOperationException(
                    $"Step '{step}' is disarmed and cannot be started.");
            }

            string phase = InstallationStepCatalog.PhaseOf(step);
            EnsureNotTerminal();
            if (State.Status == InstallationStatus.RollbackRunning)
                throw new InvalidOperationException("Installation steps cannot start during rollback.");
            if (!string.IsNullOrEmpty(State.ActiveStep))
                throw new InvalidOperationException($"Step '{State.ActiveStep}' is already active.");
            if (State.CompletedSteps.Contains(step, StringComparer.Ordinal))
                throw new InvalidOperationException($"Step '{step}' is already complete.");

            string expected = GetNextExpectedStepId()
                ?? throw new InvalidOperationException("No further step is expected.");
            if (!string.Equals(expected, step, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Step '{step}' is out of order; expected '{expected}'.");
            }

            if (State.Status == InstallationStatus.Failed)
                State.Failure = null;

            State.Phase = phase;
            State.ActiveStep = step;
            State.Status = InstallationStatus.Running;
            Touch();
        }

        /// <summary>
        /// Marks an optional inapplicable step complete without activating it (dual-boot
        /// skips staging; interactive install skips autoinstall config).
        /// </summary>
        public void SkipOptionalStep(string step)
        {
            InstallationStepDefinition definition = InstallationStepCatalog.Get(step);
            if (definition.Required)
            {
                throw new InvalidOperationException(
                    $"Required step '{step}' cannot be skipped.");
            }
            if (!definition.Armed)
            {
                throw new InvalidOperationException(
                    $"Disarmed step '{step}' cannot be skipped through the armed path.");
            }

            EnsureNotTerminal();
            if (!string.IsNullOrEmpty(State.ActiveStep))
                throw new InvalidOperationException($"Step '{State.ActiveStep}' is already active.");
            if (State.CompletedSteps.Contains(step, StringComparer.Ordinal))
                return;

            string expected = GetNextExpectedStepId()
                ?? throw new InvalidOperationException("No further step is expected.");
            if (!string.Equals(expected, step, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Step '{step}' is out of order; expected '{expected}'.");
            }

            if (State.Status == InstallationStatus.Failed)
                State.Failure = null;

            State.CompletedSteps.Add(step);
            State.Status = InstallationStatus.Running;
            State.Phase = InstallationStepCatalog.PhaseOf(step);
            Touch();
        }

        public void CompleteStep(string step)
        {
            if (!string.Equals(State.Status, InstallationStatus.Running, StringComparison.Ordinal))
                throw new InvalidOperationException("Only a running installation can complete a step.");
            if (!string.Equals(State.ActiveStep, step, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot complete '{step}'; active step is '{State.ActiveStep}'.");
            }

            State.CompletedSteps.Add(step);
            State.ActiveStep = null;
            Touch();
        }

        /// <summary>
        /// own-linux-installer: este método é chamado de um único lugar em C#
        /// (<c>InstallationFlowRunner.Run</c>), e SÓ para os mecanismos cuja instalação termina
        /// do lado Windows — Subiquity, UbiquityPreseed, Archinstall, dual-boot manual, modo
        /// substituir. Para esses, o boot ficar preparado É o fim do que este processo
        /// acompanha; o resto acontece dentro do instalador nativo da distro, depois do reboot,
        /// fora do nosso controle.
        ///
        /// Por isso a checagem de "passo obrigatório incompleto" olha só a fase
        /// <c>windows</c>. Os passos <c>live.*</c>/<c>target.*</c> (D12) são reais e
        /// obrigatórios — mas só para o mecanismo <c>OwnLiveInstaller</c>, cujo fim de
        /// transação NUNCA passa por este método: é o instalador live (bash, pós-reboot) quem
        /// escreve o estado terminal direto no registro espelhado, depois de completar esses
        /// passos em sequência (e falhar antes disso se qualquer fase falhar, via `set -e`).
        /// Validá-los aqui bloquearia toda instalação Subiquity/Ubiquity/Archinstall, que nunca
        /// inicia nem vai iniciar um passo live/target — foi exatamente o bug real encontrado
        /// ao testar dual-boot desatendido do Ubuntu numa VM (armar os passos quebrou o
        /// MarkSucceeded de todo mecanismo, não só do novo).
        /// </summary>
        public void MarkSucceeded()
        {
            if (!string.IsNullOrEmpty(State.ActiveStep))
            {
                throw new InvalidOperationException(
                    $"Cannot succeed while step '{State.ActiveStep}' is still active.");
            }

            foreach (InstallationStepDefinition step in InstallationStepCatalog.All)
            {
                if (step.Required && step.Armed &&
                    InstallationStepCatalog.PhaseOf(step.Id) == InstallationPhase.Windows &&
                    !State.CompletedSteps.Contains(step.Id, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot succeed: required armed step '{step.Id}' is incomplete.");
                }
            }

            State.Status = InstallationStatus.Succeeded;
            State.Phase = InstallationPhase.Complete;
            State.Failure = null;
            Touch();
        }

        public void Fail(string code, string message, string component)
        {
            EnsureNotTerminal();
            if (State.Status == InstallationStatus.RollbackRunning)
                throw new InvalidOperationException("Use rollback completion to report a rollback result.");
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Failure code and message are required.");
            if (component is not (
                InstallationPhase.Windows or InstallationPhase.Live or
                InstallationPhase.Target or InstallationPhase.Rollback))
            {
                throw new ArgumentException("Unknown failure component.", nameof(component));
            }

            State.Status = InstallationStatus.Failed;
            State.Failure = new InstallationFailure
            {
                Code = code,
                Message = message,
                Component = component,
            };
            // Keep ActiveStep — it marks the interrupted step as a compensation candidate (D4).
            Touch();
        }

        public void BeginRollback()
        {
            if (State.Status is not (InstallationStatus.Failed or InstallationStatus.Running))
            {
                throw new InvalidOperationException(
                    "Rollback can only begin from a running or failed installation.");
            }

            State.Status = InstallationStatus.RollbackRunning;
            State.Phase = InstallationPhase.Rollback;
            // Keep ActiveStep until compensated — interrupted mid-step is still a candidate (D4).
            Touch();
        }

        /// <summary>
        /// Compensatable steps that were started (completed or still active), newest first.
        /// </summary>
        public IReadOnlyList<string> GetCompensationCandidates()
        {
            var candidates = new List<string>();
            for (int i = InstallationStepCatalog.All.Count - 1; i >= 0; i--)
            {
                InstallationStepDefinition step = InstallationStepCatalog.All[i];
                if (!step.Compensatable)
                    continue;
                if (State.CompensatedSteps.Contains(step.Id, StringComparer.Ordinal))
                    continue;

                bool started =
                    State.CompletedSteps.Contains(step.Id, StringComparer.Ordinal) ||
                    string.Equals(State.ActiveStep, step.Id, StringComparison.Ordinal);

                if (started)
                    candidates.Add(step.Id);
            }

            return candidates;
        }

        public void CompleteCompensation(string step)
        {
            if (!string.Equals(State.Status, InstallationStatus.RollbackRunning, StringComparison.Ordinal))
                throw new InvalidOperationException("Compensations can only complete during rollback.");

            InstallationStepDefinition definition = InstallationStepCatalog.Get(step);
            if (!definition.Compensatable)
                throw new InvalidOperationException($"Step '{step}' is not compensatable.");

            bool started =
                State.CompletedSteps.Contains(step, StringComparer.Ordinal) ||
                string.Equals(State.ActiveStep, step, StringComparison.Ordinal);

            if (!started)
            {
                throw new InvalidOperationException(
                    $"Cannot compensate step '{step}' that never started.");
            }

            if (!State.CompensatedSteps.Contains(step, StringComparer.Ordinal))
                State.CompensatedSteps.Add(step);

            if (string.Equals(State.ActiveStep, step, StringComparison.Ordinal))
                State.ActiveStep = null;

            Touch();
        }

        public void CompleteRollback()
        {
            if (!string.Equals(State.Status, InstallationStatus.RollbackRunning, StringComparison.Ordinal))
                throw new InvalidOperationException("No rollback is running.");

            IReadOnlyList<string> pending = GetCompensationCandidates();
            if (pending.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Rollback cannot complete before compensating '{pending[0]}'.");
            }

            State.Status = InstallationStatus.RolledBack;
            State.Phase = InstallationPhase.Complete;
            State.ActiveStep = null;
            Touch();
        }

        /// <summary>
        /// Leaves the transaction inspectable after an incomplete compensation (D7).
        /// Does not promote partial cleanup to verified rollback.
        /// </summary>
        public void MarkRollbackIncomplete(string code, string message)
        {
            if (!string.Equals(State.Status, InstallationStatus.RollbackRunning, StringComparison.Ordinal))
                throw new InvalidOperationException("No rollback is running.");
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Failure code and message are required.");

            State.Status = InstallationStatus.Failed;
            State.Phase = InstallationPhase.Rollback;
            State.Failure = new InstallationFailure
            {
                Code = code,
                Message = message,
                Component = InstallationPhase.Rollback,
            };
            Touch();
        }

        public string? GetNextPendingArmedStepId()
        {
            if (!string.IsNullOrEmpty(State.ActiveStep))
                return State.ActiveStep;

            foreach (InstallationStepDefinition step in InstallationStepCatalog.All)
            {
                if (!step.Armed)
                    continue;
                if (State.CompletedSteps.Contains(step.Id, StringComparer.Ordinal))
                    continue;
                return step.Id;
            }

            return null;
        }

        public static void ValidateState(InstallationExecutionState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            if (state.SchemaVersion != InstallationExecutionState.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported installation-state schemaVersion {state.SchemaVersion}.");
            }
            if (!IsHexId(state.PlanId))
                throw new InvalidOperationException("planId must be 32 lowercase hex characters.");
            if (state.Revision < 0)
                throw new InvalidOperationException("revision cannot be negative.");
            if (state.UpdatedAtUtc == default || state.UpdatedAtUtc.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("updatedAtUtc must be UTC.");

            if (state.ActiveStep is not null)
            {
                if (!StepPattern.IsMatch(state.ActiveStep))
                    throw new InvalidOperationException($"activeStep '{state.ActiveStep}' is invalid.");
                _ = InstallationStepCatalog.Get(state.ActiveStep);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string step in state.CompletedSteps)
            {
                if (!StepPattern.IsMatch(step) || !seen.Add(step))
                    throw new InvalidOperationException($"completedSteps contains invalid entry '{step}'.");
                _ = InstallationStepCatalog.Get(step);
            }

            foreach (string step in state.CompensatedSteps)
            {
                if (!StepPattern.IsMatch(step))
                    throw new InvalidOperationException($"compensatedSteps contains invalid entry '{step}'.");
                _ = InstallationStepCatalog.Get(step);
            }
        }

        private string? GetNextExpectedStepId()
        {
            int completed = State.CompletedSteps.Count;
            if (completed >= InstallationStepCatalog.All.Count)
                return null;

            // Next catalog entry in order — including optional and disarmed ones.
            // StartStep / SkipOptionalStep enforce Armed / Required themselves.
            return InstallationStepCatalog.All[completed].Id;
        }

        private void EnsureNotTerminal()
        {
            if (State.Status is InstallationStatus.Succeeded or InstallationStatus.RolledBack)
            {
                throw new InvalidOperationException(
                    $"Installation is already terminal ({State.Status}).");
            }
        }

        private void Touch()
        {
            State.Revision++;
            State.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        private static bool IsHexId(string? value) =>
            !string.IsNullOrEmpty(value) &&
            value.Length == 32 &&
            value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}
