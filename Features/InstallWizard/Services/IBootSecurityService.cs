namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// BitLocker (or equivalent) state as conversion status + percent — never a boolean (task 6.4).
    /// </summary>
    public sealed record VolumeEncryptionState(
        string ConversionStatus,
        double PercentComplete,
        int ProtectionStatus,
        bool QuerySucceeded)
    {
        /// <summary>
        /// True only when the volume is fully decrypted and unprotected. Suspended protection
        /// with remaining ciphertext is NOT treated as clear.
        /// </summary>
        public bool IsFullyClear =>
            QuerySucceeded &&
            ProtectionStatus == 0 &&
            PercentComplete <= 0.01 &&
            string.Equals(ConversionStatus, "FullyDecrypted", StringComparison.OrdinalIgnoreCase);

        public bool BlocksStagingBoot => QuerySucceeded && !IsFullyClear;
    }

    public interface IBootSecurityService
    {
        bool IsSecureBootEnabled();

        /// <summary>
        /// Reads conversion status and percent. A failed query must be treated as refusal —
        /// never as "no encryption".
        /// </summary>
        VolumeEncryptionState GetVolumeEncryptionState(char driveLetter);
    }
}
