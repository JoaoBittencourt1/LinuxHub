using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class OwnLiveMediaBootEntryBuilderTests
    {
        [Fact]
        public void Build_LoopsBackTheLiveMediaIso_AndBootsOwnKernel()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build(@"C:\ProgramData\LinuxHub\LiveMedia\linuxhub-live.iso");

            Assert.Contains("loopback loop $isofile", cfg);
            Assert.Contains("(loop)/live/vmlinuz", cfg);
            Assert.Contains("(loop)/live/initrd.img", cfg);
            // D1: nenhum parâmetro de instalação desatendida na linha de boot — o instalador
            // live descobre tudo a partir do plano publicado no disco (D13), não da linha de kernel.
            Assert.DoesNotContain("autoinstall", cfg);
            Assert.DoesNotContain("automatic-ubiquity", cfg);
        }

        /// <summary>
        /// Bug real: o caminho chega do lado C# como caminho do Windows, mas o GRUB não conhece
        /// letra de unidade nem barra invertida — emitir o caminho cru faria a máquina
        /// reiniciar num GRUB incapaz de achar a própria mídia live.
        /// </summary>
        [Fact]
        public void Build_ConvertsTheWindowsPathToAGrubPath()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build(@"C:\ProgramData\LinuxHub\LiveMedia\linuxhub-live.iso");

            Assert.Contains(@"set isofile=""/ProgramData/LinuxHub/LiveMedia/linuxhub-live.iso""", cfg);
            Assert.DoesNotContain("C:", cfg);
            Assert.DoesNotContain(@"\", cfg);
        }

        /// <summary>
        /// GRUB é parser de herança Unix: um '\r' no fim do valor de $isofile faz o
        /// <c>search --file</c> procurar um arquivo que não existe. Mesma proteção que
        /// GrubConfigBuilder já tinha, e que faltava neste caminho.
        /// </summary>
        [Fact]
        public void Build_EmitsUnixLineEndingsOnly()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build(@"C:\linuxhub-live.iso");

            Assert.DoesNotContain("\r", cfg);
        }

        [Fact]
        public void Build_RejectsEmptyPath()
        {
            Assert.Throws<ArgumentException>(() => OwnLiveMediaBootEntryBuilder.Build(""));
        }
    }
}
