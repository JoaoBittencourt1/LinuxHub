using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IInstallationExecutionLedger
    {
        string StatePath { get; }

        InstallationExecutionState State { get; }

        void StartStep(string step);

        void SkipOptionalStep(string step);

        void CompleteStep(string step);

        void SetProgress(string stage, int overallPercent, int? detailPercent = null);

        void Fail(string code, string message, string component);

        void MarkSucceeded();

        void BeginRollback();

        IReadOnlyList<string> GetCompensationCandidates();

        void CompleteCompensation(string step);

        void CompleteRollback();

        void MarkRollbackIncomplete(string code, string message);

        string? GetNextPendingArmedStepId();
    }

    public interface IInstallationExecutionLedgerFactory
    {
        IInstallationExecutionLedger Create(string planId, string statePath);

        IInstallationExecutionLedger Open(string statePath);
    }

    public sealed class InstallationExecutionLedgerFactory : IInstallationExecutionLedgerFactory
    {
        public IInstallationExecutionLedger Create(string planId, string statePath) =>
            InstallationExecutionLedger.Create(planId, statePath);

        public IInstallationExecutionLedger Open(string statePath) =>
            InstallationExecutionLedger.Open(statePath);
    }

    /// <summary>
    /// Persists legal installation transitions atomically as plain JSON (D13.3).
    /// </summary>
    public sealed class InstallationExecutionLedger : IInstallationExecutionLedger
    {
        internal static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        private InstallationStateMachine _stateMachine;

        private InstallationExecutionLedger(string statePath, InstallationStateMachine stateMachine)
        {
            if (string.IsNullOrWhiteSpace(statePath))
                throw new ArgumentException("An execution state path is required.", nameof(statePath));

            StatePath = statePath;
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        }

        public string StatePath { get; }

        public InstallationExecutionState State => _stateMachine.State;

        public static InstallationExecutionLedger Create(string planId, string statePath)
        {
            var ledger = new InstallationExecutionLedger(
                statePath,
                InstallationStateMachine.Create(planId));
            ledger.Persist();
            return ledger;
        }

        public static InstallationExecutionLedger Open(string statePath)
        {
            InstallationExecutionState state = Read(statePath);
            return new InstallationExecutionLedger(statePath, new InstallationStateMachine(state));
        }

        public void StartStep(string step)
        {
            _stateMachine.StartStep(step);
            Persist();
        }

        public void SkipOptionalStep(string step)
        {
            _stateMachine.SkipOptionalStep(step);
            Persist();
        }

        public void CompleteStep(string step)
        {
            _stateMachine.CompleteStep(step);
            Persist();
        }

        public void SetProgress(string stage, int overallPercent, int? detailPercent = null)
        {
            _stateMachine.SetProgress(stage, overallPercent, detailPercent);
            Persist();
        }

        public void Fail(string code, string message, string component)
        {
            _stateMachine.Fail(code, message, component);
            Persist();
        }

        public void MarkSucceeded()
        {
            _stateMachine.MarkSucceeded();
            Persist();
        }

        public void BeginRollback()
        {
            _stateMachine.BeginRollback();
            Persist();
        }

        public IReadOnlyList<string> GetCompensationCandidates() =>
            _stateMachine.GetCompensationCandidates();

        public void CompleteCompensation(string step)
        {
            _stateMachine.CompleteCompensation(step);
            Persist();
        }

        public void CompleteRollback()
        {
            _stateMachine.CompleteRollback();
            Persist();
        }

        public void MarkRollbackIncomplete(string code, string message)
        {
            _stateMachine.MarkRollbackIncomplete(code, message);
            Persist();
        }

        public string? GetNextPendingArmedStepId() => _stateMachine.GetNextPendingArmedStepId();

        public static InstallationExecutionState Read(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            InstallationExecutionState? state;
            try
            {
                state = JsonSerializer.Deserialize<InstallationExecutionState>(
                    File.ReadAllText(path), SerializerOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"Installation state JSON is invalid: {exception.Message}", exception);
            }

            if (state is null)
                throw new InvalidOperationException("Installation state JSON deserialized to null.");

            InstallationStateMachine.ValidateState(state);
            return state;
        }

        private void Persist()
        {
            InstallationStateMachine.ValidateState(State);
            AtomicJsonFile.Write(StatePath, JsonSerializer.Serialize(State, SerializerOptions));
        }
    }
}
