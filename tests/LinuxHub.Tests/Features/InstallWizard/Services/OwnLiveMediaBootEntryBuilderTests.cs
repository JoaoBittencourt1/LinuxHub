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
        /// Bug real, encontrado em VM: o GRUB acha o arquivo na NTFS e dá chainload no kernel
        /// (search --file + loopback), mas isso só entrega o KERNEL — o live-boot, já rodando
        /// dentro do initramfs, tem sua PRÓPRIA busca pelo sistema live, e por padrão só varre
        /// mídia óptica/USB. Sem fromiso= dizendo onde está o arquivo, ele morre com "Unable to
        /// find a medium containing a live file system" mesmo com o kernel certo já carregado.
        /// Mesmo papel que iso-scan/filename= cumpre para o Casper.
        /// </summary>
        [Fact]
        public void Build_TellsLiveBootWhereTheIsoFileIs()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build(@"C:\ProgramData\LinuxHub\LiveMedia\linuxhub-live.iso");

            Assert.Contains("fromiso=$isofile", cfg);

            // fromiso= precisa vir na linha "linux", antes de qualquer coisa depois de boot=live
            // — é parâmetro de kernel, não de initrd.
            int linuxLineIndex = cfg.IndexOf("linux (loop)/live/vmlinuz", StringComparison.Ordinal);
            int fromIsoIndex = cfg.IndexOf("fromiso=", StringComparison.Ordinal);
            int lineEnd = cfg.IndexOf('\n', linuxLineIndex);
            Assert.InRange(fromIsoIndex, linuxLineIndex, lineEnd);
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
