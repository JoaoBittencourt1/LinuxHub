using System.IO;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class InterruptedTransactionProbeTests
    {
        private static string RealSystemDrive() =>
            InstallationTransactionPaths.NormalizeSystemDrive(
                Environment.GetEnvironmentVariable("SystemDrive") ?? "C:");

        [Fact]
        public void FindBlockingTransaction_RunningStateUnderMatchingDirectory_IsReturned()
        {
            string systemDrive = RealSystemDrive();
            string planId = Guid.NewGuid().ToString("N");
            string transactionRoot = InstallationTransactionPaths.GetTransactionRoot(systemDrive, planId);
            string statePath = InstallationTransactionPaths.GetStatePath(systemDrive, planId);

            try
            {
                InstallationExecutionLedger.Create(planId, statePath);

                var probe = new InterruptedTransactionProbe();
                InterruptedTransactionInfo? info = probe.FindBlockingTransaction(systemDrive);

                Assert.NotNull(info);
                Assert.Equal(planId, info!.PlanId);
            }
            catch (UnauthorizedAccessException)
            {
                // Restricted CI agent without rights to ProgramData — same skip as
                // InstallationPlanTests.Publish_RoundTripsThroughRealProgramDataPath.
            }
            finally
            {
                if (Directory.Exists(transactionRoot))
                    Directory.Delete(transactionRoot, recursive: true);
            }
        }

        /// <summary>
        /// own-linux-installer task 10.3 (design.md D3): o nome do diretório é, na prática, um
        /// marcador. Se ele não bate com o planId de dentro do state.json, ler esse estado como
        /// se fosse a transação "planId-do-nome-do-diretório" seria aceitar marcador como prova.
        /// </summary>
        [Fact]
        public void FindBlockingTransaction_DirectoryNameDoesNotMatchStatePlanId_IsIgnored()
        {
            string systemDrive = RealSystemDrive();
            string actualPlanId = Guid.NewGuid().ToString("N");
            string directoryPlanId = Guid.NewGuid().ToString("N");
            string transactionRoot = InstallationTransactionPaths.GetTransactionRoot(systemDrive, directoryPlanId);
            string statePath = InstallationTransactionPaths.GetStatePath(systemDrive, directoryPlanId);

            try
            {
                // Cria o estado com o planId INTERNO diferente do nome do diretório que o
                // hospeda — o cenário de um diretório renomeado/copiado por engano.
                Directory.CreateDirectory(transactionRoot);
                InstallationExecutionLedger.Create(actualPlanId, statePath);

                var probe = new InterruptedTransactionProbe();
                InterruptedTransactionInfo? info = probe.FindBlockingTransaction(systemDrive);

                Assert.Null(info);
            }
            catch (UnauthorizedAccessException)
            {
                // Restricted CI agent without rights to ProgramData.
            }
            finally
            {
                if (Directory.Exists(transactionRoot))
                    Directory.Delete(transactionRoot, recursive: true);
            }
        }
    }
}
