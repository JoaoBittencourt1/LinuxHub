namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Disk mutation services consult this before writing. A missing or mismatched plan
    /// rejects the operation with no I/O (installation-plan spec).
    /// </summary>
    public interface IInstallationPlanMutationGuard
    {
        void EnsurePublishedForDisk(int diskIndex);
    }

    public sealed class InstallationPlanMutationGuard : IInstallationPlanMutationGuard
    {
        private readonly IInstallationPlanPublisher _publisher;

        public InstallationPlanMutationGuard(IInstallationPlanPublisher publisher)
        {
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        }

        public void EnsurePublishedForDisk(int diskIndex)
        {
            var plan = _publisher.Current
                ?? throw new InvalidOperationException(
                    "Disk mutation rejected: no installation plan is published for the current transaction.");

            InstallationPlanValidator.Validate(plan);

            if (plan.Disk.Number != diskIndex)
            {
                throw new InvalidOperationException(
                    $"Disk mutation rejected: published plan targets disk {plan.Disk.Number}, not {diskIndex}.");
            }
        }
    }

    /// <summary>Test double that never blocks — production never uses this.</summary>
    public sealed class PermissiveInstallationPlanMutationGuard : IInstallationPlanMutationGuard
    {
        public void EnsurePublishedForDisk(int diskIndex)
        {
        }
    }
}
