namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IRecoveryAgentRegistrar
    {
        bool IsArmed { get; }

        /// <summary>
        /// Registers the AtStartup SYSTEM recovery task and confirms by reading it back (D5).
        /// Failure must abort installation before any mutation.
        /// </summary>
        void Register(string planId, string transactionRootWindows);

        /// <summary>
        /// Removes the scheduled task after success or after compensation finishes (task 5.8).
        /// </summary>
        void Unregister(string planId);
    }

    /// <summary>
    /// Production registrar until phase 8 — registration is unreachable (task 5.9).
    /// </summary>
    public sealed class DisarmedRecoveryAgentRegistrar : IRecoveryAgentRegistrar
    {
        public bool IsArmed => false;

        public void Register(string planId, string transactionRootWindows)
        {
            throw new InvalidOperationException(
                "Recovery agent registration is disarmed until VM validation " +
                "(InstallationSafetySwitches.RecoveryAndCompensationArmed, constitution §7.1).");
        }

        public void Unregister(string planId)
        {
            // Safe no-op: nothing was registered while disarmed.
        }
    }

    /// <summary>
    /// Armed registrar that still refuses to run while the safety switch is off.
    /// Constructed only from tests until phase 8 flips the switch and App composition.
    /// </summary>
    public sealed class RecoveryAgentRegistrar : IRecoveryAgentRegistrar
    {
        private readonly Func<string, string, string> _runRegisterScript;
        private readonly Func<string, string> _runUnregisterScript;
        private readonly Func<string, bool> _taskExists;
        private readonly Func<bool> _isArmed;

        public RecoveryAgentRegistrar(
            Func<string, string, string>? runRegisterScript = null,
            Func<string, string>? runUnregisterScript = null,
            Func<string, bool>? taskExists = null,
            Func<bool>? isArmed = null)
        {
            _runRegisterScript = runRegisterScript ?? DefaultRegister;
            _runUnregisterScript = runUnregisterScript ?? DefaultUnregister;
            _taskExists = taskExists ?? DefaultTaskExists;
            _isArmed = isArmed ?? (() => InstallationSafetySwitches.RecoveryAndCompensationArmed);
        }

        public bool IsArmed => _isArmed();

        public void Register(string planId, string transactionRootWindows)
        {
            EnsureArmed();
            if (string.IsNullOrWhiteSpace(planId))
                throw new ArgumentException("planId is required.", nameof(planId));
            if (string.IsNullOrWhiteSpace(transactionRootWindows))
                throw new ArgumentException("transaction root is required.", nameof(transactionRootWindows));

            string taskName = TaskNameFor(planId);
            string agentPath = ScriptCatalog.GetPath(ScriptCatalog.RecoveryAgent);
            string output = _runRegisterScript(taskName, agentPath);

            if (!_taskExists(taskName))
            {
                throw new InvalidOperationException(
                    $"Recovery task '{taskName}' was not confirmed after registration. Output: {output}");
            }
        }

        public void Unregister(string planId)
        {
            EnsureArmed();
            string taskName = TaskNameFor(planId);
            _ = _runUnregisterScript(taskName);
            if (_taskExists(taskName))
                throw new InvalidOperationException($"Recovery task '{taskName}' still exists after unregister.");
        }

        public static string TaskNameFor(string planId) => $"LinuxHub-Recovery-{planId}";

        private void EnsureArmed()
        {
            if (!_isArmed())
            {
                throw new InvalidOperationException(
                    "Recovery agent path is disarmed (constitution §7.1).");
            }
        }

        private static string DefaultRegister(string taskName, string agentPath)
        {
            string scriptPath = ScriptCatalog.GetPath(ScriptCatalog.RegisterRecoveryTask);
            return ElevatedPowerShellRunner.RunFile(
                scriptPath,
                $"-TaskName \"{Escape(taskName)}\" -AgentPath \"{Escape(agentPath)}\"",
                "register recovery agent");
        }

        private static string DefaultUnregister(string taskName)
        {
            string scriptPath = ScriptCatalog.GetPath(ScriptCatalog.UnregisterRecoveryTask);
            return ElevatedPowerShellRunner.RunFile(
                scriptPath,
                $"-TaskName \"{Escape(taskName)}\"",
                "unregister recovery agent");
        }

        private static bool DefaultTaskExists(string taskName)
        {
            string output = ElevatedPowerShellRunner.Run(
                $@"
$ErrorActionPreference = 'Stop'
$task = Get-ScheduledTask -TaskName '{Escape(taskName)}' -ErrorAction SilentlyContinue
if ($null -eq $task) {{ Write-Output 'TASK_EXISTS=false' }} else {{ Write-Output 'TASK_EXISTS=true' }}
",
                "confirm recovery task");
            return output.Contains("TASK_EXISTS=true", StringComparison.Ordinal);
        }

        private static string Escape(string value) => value.Replace("\"", "`\"", StringComparison.Ordinal);
    }
}
