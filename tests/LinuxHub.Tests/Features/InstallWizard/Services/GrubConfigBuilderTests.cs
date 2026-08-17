using System.Linq;
using LinuxHub.Common.Models;
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
            string entry = GrubConfigBuilder.BuildIsoBootEntry("Ubuntu", LiveBootSystem.Casper, @"C:\ISOs\ubuntu.iso");

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
                "Ubuntu", LiveBootSystem.Casper, @"C:\ISOs\ubuntu.iso", enableAutoinstall: true);

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
                "Ubuntu", LiveBootSystem.Casper, @"C:\ISOs\ubuntu.iso", enableAutoinstall: false);

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
                "Ubuntu", LiveBootSystem.Casper, @"C:\ISOs\ubuntu.iso", enableAutoinstall);

            string[] halves = GetKernelLine(entry).Split(" --- ");

            // Do lado do instalador: o casper-stop lê /proc/cmdline da sessão live, e o que vem
            // depois do separador pertence ao sistema instalado.
            Assert.Contains("noprompt", halves[0]);
            Assert.DoesNotContain("noprompt", halves[1]);
        }

        /// <summary>
        /// O bug que motivou a receita por distro: o gerador assumia casper para todo mundo,
        /// e a ISO do Arch não tem <c>/casper</c> nenhum — o GRUB parava com "file not found"
        /// depois do disco já ter sido reparticionado. Os caminhos conferidos aqui foram
        /// lidos da própria ISO (archlinux-2026.08.01-x86_64).
        /// </summary>
        [Fact]
        public void BuildIsoBootEntry_ForArchiso_UsesTheArchKernelPathsAndNoCasper()
        {
            string entry = GrubConfigBuilder.BuildIsoBootEntry(
                "Arch Linux", LiveBootSystem.Archiso, @"C:\ISOs\archlinux.iso");

            Assert.Contains("linux (loop)/arch/boot/x86_64/vmlinuz-linux", entry);
            Assert.Contains("initrd (loop)/arch/boot/x86_64/initramfs-linux.img", entry);
            Assert.DoesNotContain("casper", entry);
            Assert.DoesNotContain("iso-scan/filename", entry);
            Assert.DoesNotContain(" --- ", entry);
        }

        /// <summary>
        /// O hook <c>archiso_loop_mnt</c> monta a partição hospedeira antes de abrir o
        /// loopback, então precisa identificá-la. Quem lê o UUID é o GRUB, contra o
        /// filesystem real, em tempo de boot — deduzir do lado do Windows como o Linux vai
        /// nomear o disco seria um palpite.
        /// </summary>
        [Fact]
        public void BuildIsoBootEntry_ForArchiso_LetsGrubReadTheHostFilesystemUuid()
        {
            string entry = GrubConfigBuilder.BuildIsoBootEntry(
                "Arch Linux", LiveBootSystem.Archiso, @"C:\ISOs\archlinux.iso");

            Assert.Contains("probe --set=isodevuuid --fs-uuid $root", entry);
            Assert.Contains("img_dev=UUID=$isodevuuid", entry);
            Assert.Contains("img_loop=$isofile", entry);
            Assert.Contains("archisobasedir=arch", entry);

            // O probe precisa rodar com $root ainda apontando para a partição hospedeira,
            // ou seja, depois do search e antes do loopback trocar o contexto.
            string[] lines = entry.Split('\n').Select(l => l.Trim()).ToArray();
            int search = Array.FindIndex(lines, l => l.StartsWith("search "));
            int probe = Array.FindIndex(lines, l => l.StartsWith("probe "));
            int loopback = Array.FindIndex(lines, l => l.StartsWith("loopback "));
            Assert.True(search < probe && probe < loopback);
        }

        /// <summary>
        /// Sem <c>copytoram=y</c> o hook do archiso mantém a partição do Windows montada
        /// durante toda a sessão live, e reparticionar o disco que segura a própria ISO
        /// falha com o dispositivo ocupado.
        /// </summary>
        [Fact]
        public void BuildIsoBootEntry_ForArchiso_ReleasesTheHostPartition()
        {
            string entry = GrubConfigBuilder.BuildIsoBootEntry(
                "Arch Linux", LiveBootSystem.Archiso, @"C:\ISOs\archlinux.iso");

            Assert.Contains("copytoram=y", entry);
        }

        /// <summary>
        /// Uma distro no catálogo sem receita de boot tem que falhar aqui, alto e claro. O
        /// contrário — gerar um grub.cfg com o layout de outra distro — é o bug original.
        /// </summary>
        [Fact]
        public void BuildIsoBootEntry_WithoutAValidatedRecipe_RefusesInsteadOfGuessing()
        {
            var erro = Assert.Throws<NotSupportedException>(() =>
                GrubConfigBuilder.BuildIsoBootEntry(
                    "Fedora", LiveBootSystem.Unsupported, @"C:\ISOs\fedora.iso"));

            Assert.Contains("Fedora", erro.Message);
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
            string config = GrubConfigBuilder.BuildConfig("Ubuntu", LiveBootSystem.Casper, @"C:\ISOs\ubuntu.iso", includeWindowsChainload: false);

            Assert.DoesNotContain("chainloader +1", config);
        }

        [Fact]
        public void BuildConfig_IncludesWindowsEntryWhenRequested()
        {
            string config = GrubConfigBuilder.BuildConfig("Ubuntu", LiveBootSystem.Casper, @"C:\ISOs\ubuntu.iso", includeWindowsChainload: true);

            Assert.Contains("chainloader +1", config);
        }
    }
}
