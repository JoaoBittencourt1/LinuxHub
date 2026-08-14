using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Atomic publisher for <see cref="InstallationPlan"/>. Unknown JSON fields are rejected
    /// via <see cref="JsonUnmappedMemberHandling.Disallow"/> (task 3.7).
    /// </summary>
    public sealed class InstallationPlanPublisher : IInstallationPlanPublisher
    {
        internal static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        private readonly object _gate = new();

        public InstallationPlan? Current { get; private set; }

        public string? PublishedPath { get; private set; }

        public string Publish(InstallationPlan plan, string password)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(password);

            InstallationPlanValidator.Validate(plan);

            string systemDrive = plan.Disk.SystemDrive;
            string planId = plan.PlanId;
            string transactionRoot = InstallationTransactionPaths.GetTransactionRoot(systemDrive, planId);
            string planPath = InstallationTransactionPaths.GetPlanPath(systemDrive, planId);
            string passwordPath = InstallationTransactionPaths.GetPasswordPath(systemDrive, planId);

            RequirePathMatches(
                plan.Runtime.TransactionRootWindows,
                transactionRoot,
                "runtime.transactionRootWindows");
            RequirePathMatches(
                plan.Account.PasswordWindowsPath,
                passwordPath,
                "account.passwordWindowsPath");

            Directory.CreateDirectory(transactionRoot);
            AtomicJsonFile.Write(passwordPath, password);
            AtomicJsonFile.Write(planPath, JsonSerializer.Serialize(plan, SerializerOptions));

            lock (_gate)
            {
                Current = Clone(plan);
                PublishedPath = planPath;
            }

            return planPath;
        }

        public InstallationPlan ReadValidated(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            string json = File.ReadAllText(path);
            InstallationPlan? plan;
            try
            {
                plan = JsonSerializer.Deserialize<InstallationPlan>(json, SerializerOptions);
            }
            catch (JsonException exception)
            {
                throw new InstallationPlanValidationException(
                    [$"Plan JSON is invalid: {exception.Message}"]);
            }

            if (plan is null)
                throw new InstallationPlanValidationException(["Plan JSON deserialized to null."]);

            InstallationPlanValidator.Validate(plan);
            return plan;
        }

        public void UpdateStagingIdentity(int number, long offsetBytes, string partitionUuid)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(partitionUuid);

            lock (_gate)
            {
                if (Current is null || PublishedPath is null)
                {
                    throw new InvalidOperationException(
                        "Cannot update staging identity without a published plan.");
                }

                if (Current.InstallerIdentityAlreadySet())
                {
                    throw new InvalidOperationException(
                        "Staging identity is already recorded; the plan is otherwise immutable.");
                }

                Current.Disk.Installer.Number = number;
                Current.Disk.Installer.OffsetBytes = offsetBytes;
                Current.Disk.Installer.PartitionUuid = partitionUuid;

                InstallationPlanValidator.Validate(Current);
                AtomicJsonFile.Write(
                    PublishedPath,
                    JsonSerializer.Serialize(Current, SerializerOptions));
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                Current = null;
                PublishedPath = null;
            }
        }

        private static void RequirePathMatches(string actual, string expected, string fieldName)
        {
            if (!string.Equals(
                    Path.GetFullPath(actual),
                    Path.GetFullPath(expected),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InstallationPlanValidationException(
                    [$"{fieldName} must resolve to the plan-owned transaction path."]);
            }
        }

        private static InstallationPlan Clone(InstallationPlan plan) =>
            JsonSerializer.Deserialize<InstallationPlan>(
                JsonSerializer.Serialize(plan, SerializerOptions),
                SerializerOptions)!;
    }

    file static class InstallationPlanPublisherExtensions
    {
        public static bool InstallerIdentityAlreadySet(this InstallationPlan plan) =>
            plan.Disk.Installer.Number.HasValue
            || plan.Disk.Installer.OffsetBytes.HasValue
            || plan.Disk.Installer.PartitionUuid is not null;
    }
}
