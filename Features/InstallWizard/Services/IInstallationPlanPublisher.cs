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
        /// </summary>
        void UpdateStagingIdentity(int number, long offsetBytes, string partitionUuid);

        void Clear();
    }
}
