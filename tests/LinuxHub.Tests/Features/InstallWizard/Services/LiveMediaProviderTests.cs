using System.IO;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class LiveMediaProviderTests
    {
        [Fact]
        public void GetIsoPath_PrefersTheEnvironmentOverride_WhenTheFileExists()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                var provider = new LiveMediaProvider(() => tempFile, defaultPath: "unused.iso");

                Assert.Equal(tempFile, provider.GetIsoPath());
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void GetIsoPath_EnvironmentOverridePointsToMissingFile_Throws()
        {
            string missingPath = Path.Combine(Path.GetTempPath(), $"linuxhub-live-missing-{Guid.NewGuid():N}.iso");
            var provider = new LiveMediaProvider(() => missingPath, defaultPath: "unused.iso");

            var error = Assert.Throws<FileNotFoundException>(() => provider.GetIsoPath());
            Assert.Contains(LiveMediaProvider.EnvironmentVariableName, error.Message);
        }

        [Fact]
        public void GetIsoPath_NoOverride_FallsBackToDefaultPath_WhenItExists()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                var provider = new LiveMediaProvider(() => null, defaultPath: tempFile);

                Assert.Equal(tempFile, provider.GetIsoPath());
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void GetIsoPath_NeitherOverrideNorDefaultExists_ThrowsWithBuildInstructions()
        {
            string missingDefault = Path.Combine(Path.GetTempPath(), $"linuxhub-live-missing-{Guid.NewGuid():N}.iso");
            var provider = new LiveMediaProvider(() => null, defaultPath: missingDefault);

            var error = Assert.Throws<FileNotFoundException>(() => provider.GetIsoPath());
            Assert.Contains("build-live-media.sh", error.Message);
        }
    }
}
