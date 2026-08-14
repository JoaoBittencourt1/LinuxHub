using System.IO;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed record InterruptedTransactionInfo(
        string PlanId,
        string StatePath,
        string Status,
        string? FailureCode,
        string? FailureMessage);

    public interface IInterruptedTransactionProbe
    {
        InterruptedTransactionInfo? FindBlockingTransaction(string systemDrive);
    }

    /// <summary>
    /// Finds an unresolved installation transaction that must block a new install (task 6.8).
    /// </summary>
    public sealed class InterruptedTransactionProbe : IInterruptedTransactionProbe
    {
        public InterruptedTransactionInfo? FindBlockingTransaction(string systemDrive)
        {
            string drive = InstallationTransactionPaths.NormalizeSystemDrive(systemDrive);
            string root = Path.Combine(drive + @"\", "ProgramData", "LinuxHub", "Transactions");
            if (!Directory.Exists(root))
                return null;

            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                string statePath = Path.Combine(dir, InstallationTransactionPaths.StateFileName);
                if (!File.Exists(statePath))
                    continue;

                InstallationExecutionState state;
                try
                {
                    state = InstallationExecutionLedger.Read(statePath);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (state.Status is InstallationStatus.Succeeded or InstallationStatus.RolledBack)
                    continue;

                // own-linux-installer task 10.3 (design.md D3): o nome do diretório é, na
                // prática, um marcador — e marcador sozinho nunca é prova. Sem esta checagem,
                // um diretório renomeado ou copiado por engano faria o estado de OUTRA
                // transação ser lido como se fosse a que o nome do diretório promete.
                string directoryPlanId = Path.GetFileName(dir);
                if (!string.Equals(directoryPlanId, state.PlanId, StringComparison.Ordinal))
                    continue;

                return new InterruptedTransactionInfo(
                    state.PlanId,
                    statePath,
                    state.Status,
                    state.Failure?.Code,
                    state.Failure?.Message);
            }

            return null;
        }
    }
}
