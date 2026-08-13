using System;
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
        /// Bug real, encontrado em VM (dois turnos): o GRUB acha o arquivo na NTFS e dá
        /// chainload no kernel (search --file + loopback), mas isso só entrega o KERNEL — o
        /// live-boot, já rodando dentro do initramfs, tem sua PRÓPRIA busca pelo sistema live
        /// (find_livefs em 9990-misc-helpers.sh), e por padrão só varre mídia óptica/USB.
        ///
        /// Primeira tentativa usou fromiso=, e ainda falhou — lendo o código-fonte do live-boot
        /// 20230131+deb12u1 (bookworm): fromiso= espera o NOME DO DISPOSITIVO LINUX embutido no
        /// caminho (/dev/sda3/...), que o GRUB não tem como produzir. findiso= é o parâmetro
        /// certo — varre todo dispositivo, monta, testa se o caminho RELATIVO existe, mesma
        /// semântica do search --file do GRUB e do iso-scan/filename= do Casper.
        /// </summary>
        [Fact]
        public void Build_TellsLiveBootWhereTheIsoFileIs()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build(@"C:\ProgramData\LinuxHub\LiveMedia\linuxhub-live.iso");

            Assert.Contains("findiso=$isofile", cfg);
            Assert.DoesNotContain("fromiso=", cfg);

            // findiso= precisa vir na linha "linux", antes de qualquer coisa depois de boot=live
            // — é parâmetro de kernel, não de initrd.
            int linuxLineIndex = cfg.IndexOf("linux (loop)/live/vmlinuz", StringComparison.Ordinal);
            int findIsoIndex = cfg.IndexOf("findiso=", StringComparison.Ordinal);
            int lineEnd = cfg.IndexOf('\n', linuxLineIndex);
            Assert.InRange(findIsoIndex, linuxLineIndex, lineEnd);
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
