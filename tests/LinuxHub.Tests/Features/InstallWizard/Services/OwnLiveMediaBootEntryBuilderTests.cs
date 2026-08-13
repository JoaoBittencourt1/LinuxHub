using System;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class OwnLiveMediaBootEntryBuilderTests
    {
        [Fact]
        public void Build_LoadsTheKernelDirectlyFromTheLiveMediaPartition()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build();

            Assert.Contains("/live/vmlinuz", cfg);
            Assert.Contains("/live/initrd.img", cfg);

            // D1: nenhum parâmetro de instalação desatendida na linha de boot — o instalador
            // live descobre tudo a partir do plano publicado no disco (D13), não da linha de kernel.
            Assert.DoesNotContain("autoinstall", cfg);
            Assert.DoesNotContain("automatic-ubiquity", cfg);
        }

        /// <summary>
        /// A regressão mais cara desta mudança, encontrada só em boot real: deixar a ISO como
        /// arquivo e mandar o GRUB montá-la em laço obriga o live-boot a percorrer, dentro do
        /// initramfs, uma cadeia inteira (varrer dispositivos → montar NTFS por FUSE →
        /// localizar o arquivo → criar dispositivo de laço → montar iso9660) antes de existir
        /// sistema nenhum. Cada elo é uma forma de o boot morrer sem diagnóstico possível.
        /// O kernel agora vem direto de uma partição FAT32.
        /// </summary>
        [Fact]
        public void Build_NeverLoopMountsAnIsoNorScansForOne()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build();

            Assert.DoesNotContain("loopback", cfg);
            Assert.DoesNotContain("(loop)", cfg);
            Assert.DoesNotContain("findiso", cfg);
            Assert.DoesNotContain("fromiso", cfg);
            Assert.DoesNotContain("iso9660", cfg);
        }

        /// <summary>
        /// <c>toram</c> copia a mídia para a RAM e solta o dispositivo de origem. É o mesmo
        /// problema que D0 existe para eliminar — um ambiente live segurando a partição do
        /// disco em que se vai escrever — e sem isto a mídia própria o reintroduziria.
        /// </summary>
        [Fact]
        public void Build_ReleasesTheSourceMediumWithToram()
        {
            Assert.Contains("toram", OwnLiveMediaBootEntryBuilder.Build());
        }

        /// <summary>
        /// Enquanto o mecanismo não passar a fase 11, o boot precisa ser legível: `quiet`
        /// suprime exatamente as mensagens do live-boot e do systemd que dizem onde um boot
        /// parou, e vários ciclos de teste em VM se perderam por causa disso. `console=tty1`
        /// garante que a saída vai para onde alguém está olhando.
        /// </summary>
        [Fact]
        public void Build_KeepsTheBootReadable()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build();

            Assert.DoesNotContain("quiet", cfg);
            Assert.Contains("console=tty1", cfg);
            Assert.Contains("systemd.show_status=true", cfg);
        }

        /// <summary>
        /// Bug real: sem "set timeout"/"set default" o GRUB define a entrada mas não boota nela
        /// sozinho — fica parado no menu esperando Enter, para sempre. Instalação desatendida
        /// não tem ninguém ali para apertar tecla.
        /// </summary>
        [Fact]
        public void Build_AutoBootsWithoutWaitingForAKeypress()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build();

            Assert.Contains("set timeout=0", cfg);
            Assert.Contains("set default=0", cfg);

            int menuentryIndex = cfg.IndexOf("menuentry ", StringComparison.Ordinal);
            Assert.True(cfg.IndexOf("set timeout=0", StringComparison.Ordinal) < menuentryIndex);
            Assert.True(cfg.IndexOf("set default=0", StringComparison.Ordinal) < menuentryIndex);
        }

        /// <summary>
        /// O GRUB pré-compilado embarca <c>search_fs_file</c>, e não <c>search_label</c> nem
        /// <c>search_fs_uuid</c> (Assets/Grub/README.md) — localizar a partição por rótulo
        /// falharia silenciosamente. E <c>fat</c> precisa estar carregado: a partição da mídia
        /// live é FAT32, não NTFS.
        /// </summary>
        [Fact]
        public void Build_FindsThePartitionWithTheModulesTheBootloaderActuallyHas()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build();

            Assert.Contains("search --no-floppy --file --set=root /live/vmlinuz", cfg);
            Assert.Contains("insmod fat", cfg);
            Assert.DoesNotContain("--label", cfg);
            Assert.DoesNotContain("--fs-uuid", cfg);
        }

        /// <summary>
        /// GRUB é parser de herança Unix: um '\r' no fim de uma linha faz o parse falhar ou o
        /// caminho procurado não existir.
        /// </summary>
        [Fact]
        public void Build_EmitsUnixLineEndingsOnly()
        {
            Assert.DoesNotContain("\r", OwnLiveMediaBootEntryBuilder.Build());
        }
    }
}
