using System.Linq;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Estes testes vieram do <c>GrubConfigBuilderTests</c> quando a montagem da entrada virou
    /// uma abstração — o comportamento coberto é o mesmo, só mudou onde ele mora.
    /// </summary>
    public class CasperIsoBootEntryBuilderTests
    {
        private static string Build(
            string distroName = "Ubuntu",
            string isoGrubPath = "/ISOs/ubuntu.iso",
            UnattendedBootParameters? unattended = null) =>
            CasperIsoBootEntryBuilder.Instance.Build(new IsoBootEntryRequest(
                distroName, isoGrubPath, unattended ?? UnattendedBootParameters.Interactive));

        [Fact]
        public void SearchesForIsoInsteadOfAssumingDiskNumbering()
        {
            string entry = Build();

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
        public void PutsAutoinstallBeforeTheTargetSystemSeparator()
        {
            string kernelLine = GetKernelLine(Build(unattended: Subiquity));
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
        public void WithoutAutoinstall_MatchesTheIsoOwnLoopbackRecipe()
        {
            string entry = Build(unattended: UnattendedBootParameters.Interactive);

            Assert.DoesNotContain("autoinstall", entry);
            Assert.Contains("set gfxpayload=keep", entry);
            Assert.EndsWith("--- quiet splash", GetKernelLine(entry));
        }

        /// <summary>
        /// Sem <c>noprompt</c>, o <c>/sbin/casper-stop</c> do casper para no fim da instalação
        /// com "Please remove the installation medium, then press ENTER" e trava num
        /// <c>read x &lt; /dev/console</c> — o PC nunca reinicia sozinho. Como a ISO é um
        /// arquivo no disco interno, não existe mídia para remover em nenhum dos dois modos, e
        /// o parâmetro precisa estar nos dois modos, desatendido ou não.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AlwaysDisablesTheRemoveMediumPrompt(bool unattended)
        {
            string entry = Build(unattended: unattended ? Subiquity : UnattendedBootParameters.Interactive);
            string[] halves = GetKernelLine(entry).Split(" --- ");

            // Do lado do instalador: o casper-stop lê /proc/cmdline da sessão live, e o que vem
            // depois do separador pertence ao sistema instalado.
            Assert.Contains("noprompt", halves[0]);
            Assert.DoesNotContain("noprompt", halves[1]);
        }

        /// <summary>
        /// Os dois mecanismos não podem vazar um no outro: o Ubiquity ignora <c>autoinstall</c>
        /// e o subiquity ignora <c>automatic-ubiquity</c>, então o parâmetro errado não falha —
        /// ele boota num instalador interativo esperando alguém, depois do reboot, quando o app
        /// já não pode avisar nada.
        /// </summary>
        [Fact]
        public void UbiquityMechanism_UsesItsOwnParametersAndExtraInitrd()
        {
            var ubiquity = new UnattendedBootParameters(
                IsUnattended: true,
                KernelParameters: "automatic-ubiquity",
                ExtraInitrdGrubPath: "/linuxhub-preseed.cpio");

            string entry = Build("Linux Mint", "/ISOs/mint.iso", ubiquity);

            Assert.Contains("automatic-ubiquity", GetKernelLine(entry));
            Assert.DoesNotContain("autoinstall", entry);

            // O cpio vai DEPOIS do initrd da ISO: entre arquivos de mesmo caminho no
            // initramfs concatenado, vence o último — o nosso precisa sobrepor.
            Assert.Contains("initrd (loop)/casper/initrd.lz /linuxhub-preseed.cpio", entry);
        }

        /// <summary>Sem initrd extra a linha não pode ganhar um espaço solto no fim — o GRUB
        /// trataria como um segundo caminho vazio.</summary>
        [Fact]
        public void WithoutExtraInitrd_LeavesTheInitrdLineUntouched()
        {
            string[] initrdLines = Build(unattended: Subiquity)
                .Split('\n')
                .Select(line => line.Trim('\r').TrimStart())
                .Where(line => line.StartsWith("initrd "))
                .ToArray();

            Assert.NotEmpty(initrdLines);
            Assert.All(initrdLines, line => Assert.DoesNotContain(".lz ", line));
        }

        /// <summary>
        /// O nome do initrd dentro de <c>/casper</c> não é o mesmo em toda distro — é
        /// <c>initrd</c> sem extensão no Ubuntu 24.04.4, mas <c>initrd.lz</c> no Linux Mint
        /// 22.3 (confirmado abrindo o grub.cfg real da ISO). Um valor fixo já causou boot
        /// quebrado ("VFS: Unable to mount root fs on unknown-block(0,0)") por carregar um
        /// initrd inexistente no Mint — por isso o gerado precisa testar os candidatos em
        /// tempo de boot em vez de assumir um nome só.
        /// </summary>
        [Fact]
        public void ProbesInitrdCandidatesInsteadOfAssumingOneName()
        {
            string entry = Build("Linux Mint", "/ISOs/mint.iso");

            Assert.Contains("if [ -f (loop)/casper/initrd.lz ]; then", entry);
            Assert.Contains("initrd (loop)/casper/initrd.lz", entry);
            Assert.Contains("elif [ -f (loop)/casper/initrd.img ]; then", entry);
            Assert.Contains("elif [ -f (loop)/casper/initrd.gz ]; then", entry);
            Assert.Contains("elif [ -f (loop)/casper/initrd ]; then", entry);
        }

        /// <summary>O que o preparer do subiquity devolve — os testes desta classe usam isto
        /// em vez de montar parâmetros à mão, pra que a diferença entre um mecanismo e outro
        /// fique nos testes que são sobre isso.</summary>
        private static UnattendedBootParameters Subiquity =>
            new(IsUnattended: true, KernelParameters: "autoinstall", ExtraInitrdGrubPath: null);

        private static string GetKernelLine(string entry) =>
            entry.Split('\n').Single(line => line.TrimStart().StartsWith("linux ")).Trim();
    }
}
