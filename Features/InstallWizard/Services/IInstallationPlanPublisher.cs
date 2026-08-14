using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Publishes and re-reads the installation plan. The in-memory
    /// <see cref="Current"/> reference is what mutation guards consult.
    /// </summary>
    public interface IInstallationPlanPublisher
    {
        InstallationPlan? Current { get; }

        string? PublishedPath { get; }

        /// <summary>
        /// Validates, writes the password sidecar, publishes the plan atomically, and
        /// records it as the current transaction plan.
        /// </summary>
        string Publish(InstallationPlan plan, string password);

        /// <summary>
        /// Reads and revalidates a plan from disk. Does not change <see cref="Current"/>.
        /// </summary>
        InstallationPlan ReadValidated(string path);

        /// <summary>
        /// Sole post-publish mutation: records the observed staging partition identity,
        /// revalidates the full document, and replaces atomically.
        ///
        /// <paramref name="observedSizeBytes"/> é o tamanho real da partição criada. Fica nulo
        /// no modo substituir (tamanho de política, já no plano) e é preenchido no instalador
        /// próprio, onde a partição nasce com <c>-UseMaximumSize</c> e o tamanho só passa a
        /// existir depois de criá-la — é ele que o instalador live confere antes do
        /// <c>mkfs</c>.
        /// </summary>
        void UpdateStagingIdentity(
            int number,
            long offsetBytes,
            string partitionUuid,
            long? observedSizeBytes = null);

        void Clear();
    }
}
