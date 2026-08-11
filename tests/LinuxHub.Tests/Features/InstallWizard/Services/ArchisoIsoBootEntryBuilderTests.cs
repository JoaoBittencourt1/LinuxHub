using System.Linq;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// A entrada segue o <c>configs/releng/grub/loopback.cfg</c> do pacote <c>archiso</c> que
    /// constrói a imagem — receita do fornecedor para bootar a ISO a partir de um arquivo.
    /// </summary>
    public class ArchisoIsoBootEntryBuilderTests
    {
        private const string HostPartitionUuid = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";

        private static string Build(
            UnattendedBootParameters? unattended = null,
            string hostPartitionUuid = HostPartitionUuid) =>
            ArchisoIsoBootEntryBuilder.Instance.Build(new IsoBootEntryRequest(
                "Arch Linux",
                "/ISOs/archlinux.iso",
                unattended ?? UnattendedBootParameters.Interactive,
                hostPartitionUuid));

        /// <summary>
        /// O kernel e o initramfs do archiso não moram em <c>/casper</c>, e não existe
        /// <c>boot=casper</c> nem <c>iso-scan/filename</c>. Bootar esta ISO com a entrada do
        /// casper não dá erro no Windows: dá uma tela preta depois do reboot.
        /// </summary>
        [Fact]
        public void LoadsTheArchisoKernelAndInitramfs_NotTheCasperOnes()
        {
            string entry = Build();

            Assert.Contains("linux (loop)/arch/boot/x86_64/vmlinuz-linux", entry);
            Assert.Contains("initrd (loop)/arch/boot/x86_64/initramfs-linux.img", entry);
            Assert.Contains("archisobasedir=arch", entry);
            Assert.DoesNotContain("casper", entry);
            Assert.DoesNotContain("iso-scan/filename", entry);
        }

        /// <summary>
        /// Quem abre o laço da ISO para a sessão live é o initramfs, a partir de
        /// <c>img_dev</c> e <c>img_loop</c> — o <c>loopback</c> do GRUB serve só para ele
        /// próprio conseguir ler o kernel de dentro da imagem. Sem esses dois parâmetros o
        /// archiso não acha a raiz e cai num shell de emergência.
        /// </summary>
        [Fact]
        public void NamesTheHostVolumeAndTheIsoPathForTheInitramfs()
        {
            string entry = Build();

            Assert.Contains("img_dev=PARTUUID=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", entry);
            Assert.Contains("img_loop=$isofile", entry);
        }

        /// <summary>
        /// O GRUB embutido do app não tem o comando <c>probe</c> — a imagem é gerada com uma
        /// lista fixa de módulos e não acompanha diretório de módulos para <c>insmod</c>. Um
        /// boot real morreu exatamente aí ("can't find command `probe`") e seguiu com um
        /// <c>img_dev=</c> vazio, caindo no prompt interativo do initramfs.
        /// </summary>
        [Fact]
        public void UsesNoGrubCommandOutsideTheEmbeddedModuleSet() =>
            Assert.DoesNotContain("probe", Build());

        /// <summary>Sem o identificador não há entrada possível: o initramfs do archiso não sai
        /// procurando a ISO por conta própria como o casper faz. Estourar aqui é melhor que um
        /// GRUB que cai num prompt depois do reboot.</summary>
        [Fact]
        public void WithoutTheHostPartitionUuid_Refuses() =>
            Assert.Throws<InvalidOperationException>(() => Build(hostPartitionUuid: "  "));

        [Fact]
        public void DeclaresThatItNeedsTheHostPartitionNamed() =>
            Assert.True(ArchisoIsoBootEntryBuilder.Instance.RequiresIsoHostPartitionUuid);

        /// <summary>O separador <c>---</c> é convenção do debian-installer. No archiso tudo na
        /// linha é lido pelo initramfs e pela sessão live; um <c>---</c> aqui viraria um
        /// parâmetro literal sem significado.</summary>
        [Fact]
        public void HasNoDebianInstallerSeparator() => Assert.DoesNotContain(" --- ", Build());

        [Fact]
        public void AppendsTheMechanismParametersToTheKernelLine()
        {
            string entry = Build(new UnattendedBootParameters(
                IsUnattended: true,
                KernelParameters: "script=/run/archiso/img_dev/ISOs/install.sh copytoram=n",
                ExtraInitrdGrubPath: null));

            string kernelLine = entry.Split('\n').Single(line => line.TrimStart().StartsWith("linux "));

            Assert.Contains("script=/run/archiso/img_dev/ISOs/install.sh", kernelLine);
            Assert.Contains("copytoram=n", kernelLine);
        }

        [Fact]
        public void WithoutMechanismParameters_LeavesTheKernelLineUntouched()
        {
            string kernelLine = Build().Split('\n').Single(line => line.TrimStart().StartsWith("linux "));

            Assert.EndsWith("img_loop=$isofile", kernelLine.Trim());
        }

        [Fact]
        public void DeclaresTheArchisoFamily() =>
            Assert.Equal(LiveSessionFamily.Archiso, ArchisoIsoBootEntryBuilder.Instance.Family);
    }
}
