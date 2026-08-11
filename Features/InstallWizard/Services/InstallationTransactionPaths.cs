using System.IO;
using System.Text.RegularExpressions;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Resolves the transaction directory on the system volume recorded in the plan (D13.1).
    /// Never assumes <c>C:</c>.
    ///
    /// Location (task 3.2 / design Open Question):
    /// <c>{systemDrive}\ProgramData\LinuxHub\Transactions\{planId}</c>.
    /// Same arrangement REDACTED validated: survives reboot, writable by a SYSTEM AtStartup
    /// recovery agent, and reachable once BitLocker has unlocked the Windows volume at boot.
    /// A hidden directory at the volume root was considered; ProgramData is preferred because
    /// it is the proven path and does not fight Explorer / folder-redirection heuristics.
    /// </summary>
    public static class InstallationTransactionPaths
    {
        public const string PlanFileName = "installation-plan.json";
        public const string PasswordFileName = "account-secret.env";

        private static readonly Regex PlanIdPattern = new(
            "^[0-9a-f]{32}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string GetTransactionRoot(string systemDrive, string planId)
        {
            string drive = NormalizeSystemDrive(systemDrive);
            if (!PlanIdPattern.IsMatch(planId ?? string.Empty))
                throw new ArgumentException("The installation plan identifier is invalid.", nameof(planId));

            return Path.Combine(drive + @"\", "ProgramData", "LinuxHub", "Transactions", planId);
        }

        public static string GetPlanPath(string systemDrive, string planId) =>
            Path.Combine(GetTransactionRoot(systemDrive, planId), PlanFileName);

        public static string GetPasswordPath(string systemDrive, string planId) =>
            Path.Combine(GetTransactionRoot(systemDrive, planId), PasswordFileName);

        public const string StateFileName = "installation-state.json";

        public static string GetStatePath(string systemDrive, string planId) =>
            Path.Combine(GetTransactionRoot(systemDrive, planId), StateFileName);

        public static string NormalizeSystemDrive(string systemDrive)
        {
            if (string.IsNullOrWhiteSpace(systemDrive))
                throw new ArgumentException("A Windows system drive is required.", nameof(systemDrive));

            string trimmed = systemDrive.Trim().TrimEnd('\\', '/');
            if (trimmed.Length != 2 || trimmed[1] != ':')
            {
                throw new ArgumentException(
                    "systemDrive must be a Windows drive such as C:.",
                    nameof(systemDrive));
            }

            char letter = char.ToUpperInvariant(trimmed[0]);
            if (letter is < 'A' or > 'Z')
            {
                throw new ArgumentException(
                    "systemDrive must be a Windows drive such as C:.",
                    nameof(systemDrive));
            }

            return letter + ":";
        }
    }
}
