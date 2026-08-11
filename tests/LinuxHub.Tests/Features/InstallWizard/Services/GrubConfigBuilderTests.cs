using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class GrubConfigBuilderTests
    {
        [Fact]
        public void ToGrubPath_StripsDriveLetterAndConvertsSlashes()
        {
            string result = GrubConfigBuilder.ToGrubPath(@"C:\Users\joao\AppData\Roaming\LinuxHub\ISOs\ubuntu.iso");

            Assert.Equal("/Users/joao/AppData/Roaming/LinuxHub/ISOs/ubuntu.iso", result);
        }

        [Fact]
        public void BuildWindowsChainloadEntry_SearchesForBootmgrInsteadOfAssumingPartitionIndex()
        {
            string entry = GrubConfigBuilder.BuildWindowsChainloadEntry();

            Assert.Contains("search --no-floppy --file --set=root /bootmgr", entry);
            Assert.Contains("chainloader +1", entry);
        }

        [Fact]
        public void BuildConfig_OmitsWindowsEntryWhenNotRequested()
        {
            string config = GrubConfigBuilder.BuildConfig("Ubuntu", @"C:\ISOs\ubuntu.iso", includeWindowsChainload: false);

            Assert.DoesNotContain("chainloader +1", config);
        }

        [Fact]
        public void BuildConfig_IncludesWindowsEntryWhenRequested()
        {
            string config = GrubConfigBuilder.BuildConfig("Ubuntu", @"C:\ISOs\ubuntu.iso", includeWindowsChainload: true);

            Assert.Contains("chainloader +1", config);
        }

        /// <summary>Sem construtor informado o gerador precisa continuar produzindo a entrada
        /// do casper — é o padrão declarado em <c>LiveSessionFamily</c>, e é dele que dependem
        /// todas as distros do catálogo hoje.</summary>
        [Fact]
        public void BuildConfig_WithoutAnEntryBuilder_FallsBackToCasper()
        {
            string config = GrubConfigBuilder.BuildConfig("Ubuntu", @"C:\ISOs\ubuntu.iso", includeWindowsChainload: false);

            Assert.Equal(
                config,
                GrubConfigBuilder.BuildConfig(
                    "Ubuntu",
                    @"C:\ISOs\ubuntu.iso",
                    includeWindowsChainload: false,
                    isoEntryBuilder: CasperIsoBootEntryBuilder.Instance));
        }

        /// <summary>O construtor informado é quem monta a entrada da ISO — sem isso a família
        /// declarada pela distro não teria efeito nenhum no arquivo gerado.</summary>
        [Fact]
        public void BuildConfig_UsesTheGivenEntryBuilder()
        {
            string config = GrubConfigBuilder.BuildConfig(
                "Arch Linux",
                @"C:\ISOs\archlinux.iso",
                includeWindowsChainload: false,
                isoEntryBuilder: ArchisoIsoBootEntryBuilder.Instance);

            Assert.Contains("archisobasedir=arch", config);
            Assert.DoesNotContain("boot=casper", config);
        }
    }
}
