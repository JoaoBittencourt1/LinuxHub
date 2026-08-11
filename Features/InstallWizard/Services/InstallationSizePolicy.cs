namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Size policy in GiB; observed geometry stays in exact bytes (D3).
    /// Staging size for replace mode is derived from the ISO in bytes by
    /// <see cref="IStagingPartitionService.RequiredBytesFor"/> — not a GiB policy.
    /// </summary>
    public static class InstallationSizePolicy
    {
        public const long BytesPerGiB = 1024L * 1024L * 1024L;
        public const long PartitionAlignmentBytes = 1024L * 1024L;

        /// <summary>Minimum dual-boot Linux allocation the plan accepts (GiB).</summary>
        public const int MinimumFinalSizeGiB = 8;

        public static long GiBToBytes(int gib) => checked(gib * BytesPerGiB);

        public static bool IsWholeGiB(long bytes) => bytes > 0 && bytes % BytesPerGiB == 0;
    }
}
