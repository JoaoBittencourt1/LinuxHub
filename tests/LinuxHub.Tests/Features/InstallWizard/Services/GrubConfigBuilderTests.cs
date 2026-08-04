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

        /// <summary>
        /// Sem <c>noprompt</c>, o <c>/sbin/casper-stop</c> do casper para no fim da instalação
        /// com "Please remove the installation medium, then press ENTER" e trava num
        /// <c>read x &lt; /dev/console</c> — o PC nunca reinicia sozinho. Como a ISO é um
        /// arquivo no disco interno, não existe mídia para remover em nenhum dos dois modos, e
        /// o parâmetro precisa estar dos dois lados do <c>enableAutoinstall</c>.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BuildIsoBootEntry_AlwaysDisablesTheRemoveMediumPrompt(bool enableAutoinstall)
        {
            string entry = GrubConfigBuilder.BuildIsoBootEntry(
                "Ubuntu", @"C:\ISOs\ubuntu.iso", enableAutoinstall);

            string[] halves = GetKernelLine(entry).Split(" --- ");

            // Do lado do instalador: o casper-stop lê /proc/cmdline da sessão live, e o que vem
            // depois do separador pertence ao sistema instalado.
            Assert.Contains("noprompt", halves[0]);
            Assert.DoesNotContain("noprompt", halves[1]);
        }

        private static string GetKernelLine(string entry) =>
            entry.Split('\n').Single(line => line.TrimStart().StartsWith("linux ")).Trim();

        /// <summary>
        /// O nome do initrd dentro de <c>/casper</c> não é o mesmo em toda distro — é
        /// <c>initrd</c> sem extensão no Ubuntu 24.04.4, mas <c>initrd.lz</c> no Linux Mint
        /// 22.3 (confirmado abrindo o grub.cfg real da ISO). Um valor fixo já causou boot
        /// quebrado ("VFS: Unable to mount root fs on unknown-block(0,0)") por carregar um
        /// initrd inexistente no Mint — por isso o gerado precisa testar os candidatos em
        /// tempo de boot em vez de assumir um nome só.
        /// </summary>
        [Fact]
        public void BuildIsoBootEntry_ProbesInitrdCandidatesInsteadOfAssumingOneName()
        {
            string entry = GrubConfigBuilder.BuildIsoBootEntry("Linux Mint", @"C:\ISOs\mint.iso");

            Assert.Contains("if [ -f (loop)/casper/initrd.lz ]; then", entry);
            Assert.Contains("initrd (loop)/casper/initrd.lz", entry);
            Assert.Contains("elif [ -f (loop)/casper/initrd.img ]; then", entry);
            Assert.Contains("elif [ -f (loop)/casper/initrd.gz ]; then", entry);
            Assert.Contains("elif [ -f (loop)/casper/initrd ]; then", entry);
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
    }
}
