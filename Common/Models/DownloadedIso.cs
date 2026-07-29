namespace LinuxHub.Common.Models
{
    /// <summary>Uma ISO já presente na pasta de downloads do app, pronta para instalar
    /// sem precisar baixar de novo.</summary>
    public sealed record DownloadedIso(string Path, DistroInfo? Distro, DateTime DownloadedAtUtc);
}
