using System.Linq;
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
        public void BuildIsoBootEntry_SearchesForIsoInsteadOfAssumingDiskNumbering()
        {
            string entry = GrubConfigBuilder.BuildIsoBootEntry("Ubuntu", @"C:\ISOs\ubuntu.iso");

            Assert.Contains("search --no-floppy --file --set=root $isofile", entry);
            Assert.Contains("loopback loop $isofile", entry);
            Assert.Contains("boot=casper", entry);
            Assert.Contains("iso-scan/filename=$isofile", entry);
            Assert.DoesNotContain("(hd0", entry);
        }

        /// <summary>
        /// O separador <c>---</c> divide a linha do kernel em duas metades com destinos
        /// diferentes: o que vem antes é do instalador, o que vem depois vai parar na cmdline
        /// do sistema instalado. <c>autoinstall</c> do lado errado é lido pelo sistema final
        /// (onde não significa nada) em vez do subiquity — a instalação boota e simplesmente
        /// ignora a semente.
        /// </summary>
        [Fact]
        public void BuildIsoBootEntry_PutsAutoinstallBeforeTheTargetSystemSeparator()
        {
            string entry = GrubConfigBuilder.BuildIsoBootEntry(
                "Ubuntu", @"C:\ISOs\ubuntu.iso", enableAutoinstall: true);

            string kernelLine = GetKernelLine(entry);
            string[] halves = kernelLine.Split(" --- ");

            Assert.Equal(2, halves.Length);
            Assert.Contains("autoinstall", halves[0]);
            Assert.DoesNotContain("autoinstall", halves[1]);
        }

        /// <summary>
        /// Sem autoinstall quem conduz a instalação é o usuário, então a linha tem que ser a
        /// que a própria ISO traz em <c>/boot/grub/loopback.cfg</c> — receita do fornecedor
        /// para bootar a ISO a partir de um arquivo.
        /// </summary>
        [Fact]
        public void BuildIsoBootEntry_WithoutAutoinstall_MatchesTheIsoOwnLoopbackRecipe()
        {
            string entry = GrubConfigBuilder.BuildIsoBootEntry(
                "Ubuntu", @"C:\ISOs\ubuntu.iso", enableAutoinstall: false);

            Assert.DoesNotContain("autoinstall", entry);
            Assert.Contains("set gfxpayload=keep", entry);
            Assert.EndsWith("--- quiet splash", GetKernelLine(entry));
        }

        private static string GetKernelLine(string entry) =>
            entry.Split('\n').Single(line => line.TrimStart().StartsWith("linux ")).Trim();

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
    }
}
