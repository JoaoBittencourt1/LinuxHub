using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Trava, byte a byte, o grub.cfg que o app gera hoje para as distros do catálogo — todas
    /// elas casper. Foi escrito ANTES de a montagem da entrada virar uma abstração, e existe
    /// por um motivo só: o Ubuntu é o único caminho de instalação que hoje funciona de ponta a
    /// ponta, e a única forma de provar que ele não mudou é comparar com o que ele produzia.
    ///
    /// Este teste não descreve o comportamento desejado, descreve o comportamento **existente**.
    /// Se ele falhar depois de uma mudança que não pretendia alterar o boot do Ubuntu, a
    /// mudança está errada — não o teste. Alterar o texto esperado aqui só faz sentido junto de
    /// uma decisão explícita de mudar o boot, e nunca para "fazer passar".
    /// </summary>
    public class GrubConfigCharacterizationTests
    {
        private const string IsoPath = @"C:\ISOs\ubuntu.iso";

        [Fact]
        public void Interactive_WithWindowsEntry_IsUnchanged()
        {
            string config = GrubConfigBuilder.BuildConfig(
                "Ubuntu", IsoPath, includeWindowsChainload: true);

            Assert.Equal(
                """
                set timeout=10
                set default=0

                menuentry "Instalar Ubuntu (staging LinuxHub)" {
                    insmod part_gpt
                    insmod part_msdos
                    insmod ntfs
                    insmod loopback
                    insmod iso9660
                    set gfxpayload=keep
                    set isofile="/ISOs/ubuntu.iso"
                    search --no-floppy --file --set=root $isofile
                    loopback loop $isofile
                    linux (loop)/casper/vmlinuz boot=casper iso-scan/filename=$isofile noprompt --- quiet splash
                    if [ -f (loop)/casper/initrd.lz ]; then
                        initrd (loop)/casper/initrd.lz
                    elif [ -f (loop)/casper/initrd.img ]; then
                        initrd (loop)/casper/initrd.img
                    elif [ -f (loop)/casper/initrd.gz ]; then
                        initrd (loop)/casper/initrd.gz
                    elif [ -f (loop)/casper/initrd ]; then
                        initrd (loop)/casper/initrd
                    fi
                }

                menuentry "Windows" {
                    insmod part_msdos
                    insmod ntfs
                    search --no-floppy --file --set=root /bootmgr
                    chainloader +1
                }

                """.ReplaceLineEndings("\n"),
                config);
        }

        /// <summary>O caminho que de fato roda numa instalação do Ubuntu: subiquity, UEFI
        /// (sem entrada de Windows no grub.cfg de staging).</summary>
        [Fact]
        public void Subiquity_Uefi_IsUnchanged()
        {
            string config = GrubConfigBuilder.BuildConfig(
                "Ubuntu",
                IsoPath,
                includeWindowsChainload: false,
                unattended: new UnattendedBootParameters(
                    IsUnattended: true,
                    KernelParameters: "autoinstall",
                    ExtraInitrdGrubPath: null));

            Assert.Equal(
                """
                set timeout=10
                set default=0

                menuentry "Instalar Ubuntu (staging LinuxHub)" {
                    insmod part_gpt
                    insmod part_msdos
                    insmod ntfs
                    insmod loopback
                    insmod iso9660
                    set gfxpayload=keep
                    set isofile="/ISOs/ubuntu.iso"
                    search --no-floppy --file --set=root $isofile
                    loopback loop $isofile
                    linux (loop)/casper/vmlinuz boot=casper iso-scan/filename=$isofile autoinstall noprompt --- quiet
                    if [ -f (loop)/casper/initrd.lz ]; then
                        initrd (loop)/casper/initrd.lz
                    elif [ -f (loop)/casper/initrd.img ]; then
                        initrd (loop)/casper/initrd.img
                    elif [ -f (loop)/casper/initrd.gz ]; then
                        initrd (loop)/casper/initrd.gz
                    elif [ -f (loop)/casper/initrd ]; then
                        initrd (loop)/casper/initrd
                    fi
                }

                """.ReplaceLineEndings("\n"),
                config);
        }

        /// <summary>O caminho do Mint: preseed do Ubiquity num cpio extra, que precisa vir
        /// depois do initrd da ISO na mesma linha.</summary>
        [Fact]
        public void UbiquityPreseed_ExtraInitrd_IsUnchanged()
        {
            string config = GrubConfigBuilder.BuildConfig(
                "Linux Mint",
                @"C:\ISOs\mint.iso",
                includeWindowsChainload: false,
                unattended: new UnattendedBootParameters(
                    IsUnattended: true,
                    KernelParameters: "automatic-ubiquity",
                    ExtraInitrdGrubPath: "/linuxhub-preseed.cpio"));

            Assert.Equal(
                """
                set timeout=10
                set default=0

                menuentry "Instalar Linux Mint (staging LinuxHub)" {
                    insmod part_gpt
                    insmod part_msdos
                    insmod ntfs
                    insmod loopback
                    insmod iso9660
                    set gfxpayload=keep
                    set isofile="/ISOs/mint.iso"
                    search --no-floppy --file --set=root $isofile
                    loopback loop $isofile
                    linux (loop)/casper/vmlinuz boot=casper iso-scan/filename=$isofile automatic-ubiquity noprompt --- quiet
                    if [ -f (loop)/casper/initrd.lz ]; then
                        initrd (loop)/casper/initrd.lz /linuxhub-preseed.cpio
                    elif [ -f (loop)/casper/initrd.img ]; then
                        initrd (loop)/casper/initrd.img /linuxhub-preseed.cpio
                    elif [ -f (loop)/casper/initrd.gz ]; then
                        initrd (loop)/casper/initrd.gz /linuxhub-preseed.cpio
                    elif [ -f (loop)/casper/initrd ]; then
                        initrd (loop)/casper/initrd /linuxhub-preseed.cpio
                    fi
                }

                """.ReplaceLineEndings("\n"),
                config);
        }
    }
}
