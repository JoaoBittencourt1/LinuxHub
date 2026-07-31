using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class InstallerConfigBuilderTests
    {
        private sealed class FakeSystemInfoProvider : ISystemInfoProvider
        {
            public string GetLocale() => "pt_BR.UTF-8";
            public string GetKeymap() => "br";
            public string GetTimezone() => "America/Sao_Paulo";
        }

        private sealed class FakeEspLocatorService : IEspLocatorService
        {
            public int? EspIndex { get; set; }
            public int? FindEfiSystemPartitionIndex(int diskIndex) => EspIndex;
            public EfiSystemPartitionLocation? FindSystemEfiSystemPartition() =>
                EspIndex is null ? null : new EfiSystemPartitionLocation(0, EspIndex.Value);
        }

        private static DistroInfo CreateDistro() => new DistroInfo
        {
            Id = "ubuntu",
            Name = "Ubuntu",
            Family = "debian",
            Version = "24.04"
        };

        private static BuildInstallerConfigRequest CreateRequest(bool isUefi) => new(
            Distro: CreateDistro(),
            IsoPath: @"C:\iso\ubuntu.iso",
            IsUefi: isUefi,
            Mode: InstallMode.Replace,
            TargetDiskIndex: 0,
            TargetPartitionIndex: null,
            LinuxPartitionSizeGb: 0,
            Username: "user",
            Password: "pass",
            Hostname: "host");

        [Fact]
        public void Build_Uefi_UsesEspLookup_NotFixedIndex()
        {
            var espLocator = new FakeEspLocatorService { EspIndex = 3 };
            var builder = new InstallerConfigBuilder(new FakeSystemInfoProvider(), espLocator);

            var config = builder.Build(CreateRequest(isUefi: true));

            Assert.Equal(3, config.EfiPartitionIndex);
        }

        [Fact]
        public void Build_Bios_NeverLooksUpEsp()
        {
            var espLocator = new FakeEspLocatorService { EspIndex = 3 };
            var builder = new InstallerConfigBuilder(new FakeSystemInfoProvider(), espLocator);

            var config = builder.Build(CreateRequest(isUefi: false));

            Assert.Null(config.EfiPartitionIndex);
        }
    }
}
