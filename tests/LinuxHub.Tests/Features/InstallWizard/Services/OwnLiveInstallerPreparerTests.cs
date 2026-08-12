using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class OwnLiveInstallerPreparerTests
    {
        private static InstallerConfig BuildConfig(string bootMode = "uefi") => new()
        {
            BootMode = bootMode,
            InstallMode = "dualboot",
        };

        [Fact]
        public void Mechanism_IsOwnLiveInstaller()
        {
            Assert.Equal(UnattendedInstallMechanism.OwnLiveInstaller, new OwnLiveInstallerPreparer().Mechanism);
        }

        /// <summary>D13: nenhuma partição semente — o plano já viaja por
        /// InstallationTransactionPaths antes deste preparer rodar.</summary>
        [Fact]
        public void Prepare_DualBootUefi_ReturnsNoSeedPartitionAndUnattendedBoot()
        {
            var preparer = new OwnLiveInstallerPreparer();

            UnattendedPreparationResult result = preparer.Prepare(BuildConfig(), diskIndex: 0, staging: null);

            Assert.Equal(0, result.SeedPartitionNumber);
            Assert.True(result.BootParameters.IsUnattended);
            Assert.Empty(result.BootParameters.KernelParameters);
        }

        [Fact]
        public void Prepare_NonUefi_Throws()
        {
            var preparer = new OwnLiveInstallerPreparer();

            var error = Assert.Throws<InvalidOperationException>(
                () => preparer.Prepare(BuildConfig(bootMode: "bios"), diskIndex: 0, staging: null));

            Assert.Contains("UEFI apenas", error.Message);
        }

        /// <summary>D16: só o dual-boot desatendido — modo substituir (que sempre chega aqui
        /// com uma StagingPartition não nula) continua no caminho preservado.</summary>
        [Fact]
        public void Prepare_ReplaceMode_Throws()
        {
            var preparer = new OwnLiveInstallerPreparer();
            var staging = new StagingPartition(DiskIndex: 0, PartitionNumber: 4, PartitionUuid: "aaaa-bbbb", OffsetBytes: 1024);

            var error = Assert.Throws<InvalidOperationException>(
                () => preparer.Prepare(BuildConfig(), diskIndex: 0, staging));

            Assert.Contains("modo substituir", error.Message);
        }
    }
}
