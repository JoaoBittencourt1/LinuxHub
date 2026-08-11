using System.IO;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Contract parity: the C# step catalog is the sole authority. When another runtime
    /// restates steps, this test must grow to compare copies (task 7.5 / D13).
    /// </summary>
    public class InstallationStepCatalogParityTests
    {
        [Fact]
        public void Catalog_HasStableIdsOrderAndFlags()
        {
            string[] expectedIds =
            [
                InstallationStepIds.WindowsPlanPublished,
                InstallationStepIds.WindowsDiskPrepared,
                InstallationStepIds.WindowsStagingPrepared,
                InstallationStepIds.WindowsInstallerConfigWritten,
                InstallationStepIds.WindowsTemporaryBootPrepared,
                InstallationStepIds.LiveIsoMounted,
                InstallationStepIds.LiveDistributionExtracted,
                InstallationStepIds.TargetSystemConfigured,
                InstallationStepIds.TargetBootloaderInstalled,
            ];

            Assert.Equal(expectedIds, InstallationStepCatalog.All.Select(s => s.Id).ToArray());

            Assert.True(InstallationStepCatalog.Get(InstallationStepIds.WindowsDiskPrepared).Compensatable);
            Assert.False(InstallationStepCatalog.Get(InstallationStepIds.WindowsPlanPublished).Compensatable);
            Assert.False(InstallationStepCatalog.Get(InstallationStepIds.LiveIsoMounted).Armed);
            Assert.True(InstallationStepCatalog.Get(InstallationStepIds.WindowsTemporaryBootPrepared).Armed);
        }

        [Fact]
        public void VersionedScripts_IncludeStepFacingRecoveryAgent()
        {
            Assert.Contains(
                "DISARMED",
                ScriptCatalog.Read(ScriptCatalog.RecoveryAgent),
                StringComparison.Ordinal);
            Assert.True(File.Exists(ScriptCatalog.GetPath(ScriptCatalog.CompatibilityPreflight)));
        }
    }
}
