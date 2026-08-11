using System.IO;
using System.Reflection;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Resolves versioned PowerShell under <c>Scripts/</c> at the repo/app root (D11).
    /// Scripts are not application feature code — they run outside the WPF process
    /// (recovery agent) or as reviewable elevated payloads.
    /// </summary>
    public static class ScriptCatalog
    {
        public const string RestoreMbr = "linuxhub-restore-mbr.ps1";
        public const string RemovePartitionByGeometry = "linuxhub-remove-partition-by-geometry.ps1";
        public const string RegisterRecoveryTask = "linuxhub-register-recovery-task.ps1";
        public const string UnregisterRecoveryTask = "linuxhub-unregister-recovery-task.ps1";
        public const string RecoveryAgent = "linuxhub-recovery-agent.ps1";
        public const string CompatibilityPreflight = "linuxhub-compatibility-preflight.ps1";

        public static string GetPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("A script file name is required.", nameof(fileName));

            foreach (string root in CandidateRoots())
            {
                string path = Path.Combine(root, "Scripts", fileName);
                if (File.Exists(path))
                    return path;
            }

            throw new FileNotFoundException(
                $"Versioned script '{fileName}' was not found under Scripts/.",
                fileName);
        }

        public static string Read(string fileName) => File.ReadAllText(GetPath(fileName));

        private static IEnumerable<string> CandidateRoots()
        {
            string? baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
                yield return baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string? assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(assemblyDir))
                yield return assemblyDir;

            // Walk up from BaseDirectory for test hosts that copy Scripts only at repo root.
            string? cursor = baseDir;
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(cursor); i++)
            {
                yield return cursor;
                cursor = Directory.GetParent(cursor)?.FullName;
            }
        }
    }
}
