namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Runtime arming gates for destructive capabilities that exist in code but must stay
    /// unreachable until constitution §7.1 validation completes (phase 8 VM gate).
    /// </summary>
    public static class InstallationSafetySwitches
    {
        /// <summary>
        /// Recovery-agent registration and the compensation path. Off until phase 8 proves
        /// rollback on a real VM. Implementing ahead is allowed; reaching these paths is not.
        /// </summary>
        public const bool RecoveryAndCompensationArmed = false;
    }
}
