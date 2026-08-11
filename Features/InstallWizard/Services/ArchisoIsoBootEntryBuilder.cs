using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Entrada de boot das ISOs do archiso (Arch Linux), a partir da receita do próprio
    /// fornecedor — o <c>configs/releng/grub/loopback.cfg</c> que acompanha o pacote
    /// <c>archiso</c> que constrói a imagem.
    ///
    /// A diferença estrutural em relação ao casper: aqui o GRUB **não** informa onde a ISO
    /// está por parâmetro de kernel para um script no initramfs procurar depois. Ele nomeia o
    /// volume e o caminho (<c>img_dev</c>/<c>img_loop</c>), e quem abre o laço é o hook
    /// <c>archiso_loop_mnt</c> do initramfs. O <c>loopback</c> daqui serve só para o GRUB
    /// conseguir ler o kernel e o initramfs de dentro da ISO.
    /// </summary>
    public sealed class ArchisoIsoBootEntryBuilder : IIsoBootEntryBuilder
    {
        /// <summary>Onde o hook <c>archiso_loop_mnt</c> monta o volume que hospeda a ISO. É a
        /// montagem que faz o transporte da configuração desatendida existir: ela sobrevive ao
        /// <c>switch_root</c> (entra com <c>x-initrd.mount</c>) e é por ela que o
        /// <c>.automated_script.sh</c> alcança um arquivo gravado do lado do Windows.</summary>
        public const string HostPartitionMountPoint = "/run/archiso/img_dev";

        /// <summary>O diretório base dentro da ISO. Corresponde ao <c>install_dir</c> do
        /// <c>profiledef.sh</c> do perfil releng, que é <c>arch</c> nas imagens oficiais.</summary>
        private const string InstallDirectory = "arch";

        public static ArchisoIsoBootEntryBuilder Instance { get; } = new();

        public LiveSessionFamily Family => LiveSessionFamily.Archiso;

        /// <summary>
        /// O volume que hospeda a ISO é identificado pelo UUID que o próprio GRUB lê dele em
        /// tempo de boot (<c>probe --fs-uuid</c>), e não por um valor calculado do lado do
        /// Windows. Isso é deliberado e é a parte que mais importa aqui: o <c>search --file</c>
        /// acima já encontrou o volume **pelo conteúdo**, então perguntar o UUID a esse mesmo
        /// volume garante que o initramfs vai montar exatamente o que o GRUB achou. Um
        /// identificador que o app associasse por conta própria poderia apontar para outro
        /// disco — e um <c>/dev/...</c> que exista e seja o disco errado é aceito sem
        /// questionar por qualquer instalador.
        ///
        /// É a mesma doutrina que o resto do gerador já segue: nunca um índice fixo, sempre
        /// uma busca real (design.md, D3).
        /// </summary>
        public string Build(IsoBootEntryRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var boot = request.Unattended;

            // Sem separador `---`: aquilo é convenção do debian-installer/casper. Aqui tudo o
            // que vai na linha é lido pelo initramfs do archiso e pela sessão live.
            string installerParameters = string.IsNullOrEmpty(boot.KernelParameters)
                ? string.Empty
                : " " + boot.KernelParameters;

            string extraInitrd = string.IsNullOrWhiteSpace(boot.ExtraInitrdGrubPath)
                ? string.Empty
                : " " + boot.ExtraInitrdGrubPath;

            return $@"menuentry ""Instalar {request.DistroName} (staging LinuxHub)"" {{
    insmod part_gpt
    insmod part_msdos
    insmod ntfs
    insmod loopback
    insmod iso9660
    set gfxpayload=keep
    set isofile=""{request.IsoGrubPath}""
    search --no-floppy --file --set=root $isofile
    probe --set=isodevuuid --fs-uuid $root
    loopback loop $isofile
    linux (loop)/{InstallDirectory}/boot/x86_64/vmlinuz-linux archisobasedir={InstallDirectory} img_dev=UUID=$isodevuuid img_loop=$isofile{installerParameters}
    initrd (loop)/{InstallDirectory}/boot/x86_64/initramfs-linux.img{extraInitrd}
}}
";
        }
    }
}
