using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class InstallerConfigBuilderTests
    {
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
            Hostname: "host",
            Locale: "pt_BR.UTF-8",
            Keymap: "br",
            Timezone: "America/Sao_Paulo");

        [Fact]
        public void Build_Uefi_UsesEspLookup_NotFixedIndex()
        {
            var espLocator = new FakeEspLocatorService { EspIndex = 3 };
            var builder = new InstallerConfigBuilder(espLocator);

            var config = builder.Build(CreateRequest(isUefi: true));

            Assert.Equal(3, config.EfiPartitionIndex);
        }

        [Fact]
        public void Build_Bios_NeverLooksUpEsp()
        {
            var espLocator = new FakeEspLocatorService { EspIndex = 3 };
            var builder = new InstallerConfigBuilder(espLocator);

            var config = builder.Build(CreateRequest(isUefi: false));

            Assert.Null(config.EfiPartitionIndex);
        }

        /// <summary>
        /// Os três campos regionais saem do pedido, não de uma leitura do sistema feita aqui
        /// dentro: quem os apresenta e permite corrigir é o passo regional do wizard, e ler de
        /// novo neste ponto abriria divergência entre o que o usuário viu e o que foi gravado.
        /// </summary>
        [Fact]
        public void Build_CarriesTheRegionalSettingsOfTheRequest()
        {
            var builder = new InstallerConfigBuilder(new FakeEspLocatorService());

            var config = builder.Build(CreateRequest(isUefi: true) with
            {
                Locale = "de_DE.UTF-8",
                Keymap = "de",
                Timezone = "Europe/Berlin"
            });

            Assert.Equal("de_DE.UTF-8", config.Locale);
            Assert.Equal("de", config.Keymap);
            Assert.Equal("Europe/Berlin", config.Timezone);
        }
    }
}
