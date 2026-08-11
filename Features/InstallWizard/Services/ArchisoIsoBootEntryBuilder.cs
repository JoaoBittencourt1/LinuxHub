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

        public bool RequiresIsoHostPartitionUuid => true;

        /// <summary>
        /// O volume que hospeda a ISO é nomeado pelo PARTUUID que o app descobriu do lado do
        /// Windows (<see cref="IsoHostPartitionLocator"/>), e o hook <c>archiso_loop_mnt</c>
        /// repassa a tag <c>PARTUUID=</c> direto para o <c>mount</c>.
        ///
        /// A primeira versão disto perguntava o UUID ao próprio GRUB em tempo de boot
        /// (<c>probe --fs-uuid</c>), o que seria melhor por não depender de nada calculado no
        /// Windows. Um boot real derrubou a ideia: o GRUB embutido do app é gerado com uma
        /// lista fixa de módulos, sem <c>probe</c> e sem diretório de módulos para
        /// <c>insmod</c> — a entrada morria em "can't find command `probe`" e seguia com um
        /// <c>img_dev=</c> vazio.
        /// </summary>
        public string Build(IsoBootEntryRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.IsoHostPartitionUuid))
            {
                // Sem o identificador não há entrada de boot possível: o initramfs do archiso
                // não sai procurando a ISO por conta própria como o casper faz. Estourar aqui
                // é o resultado bom — a alternativa é um GRUB que cai num prompt interativo
                // depois do reboot.
                throw new InvalidOperationException(
                    "A entrada de boot do Arch precisa do identificador da partição onde a ISO " +
                    "está, e ele não foi informado.");
            }

            var boot = request.Unattended;

            // Sem separador `---`: aquilo é convenção do debian-installer/casper. Aqui tudo o
            // que vai na linha é lido pelo initramfs do archiso e pela sessão live.
            string installerParameters = string.IsNullOrEmpty(boot.KernelParameters)
                ? string.Empty
                : " " + boot.KernelParameters;

            string extraInitrd = string.IsNullOrWhiteSpace(boot.ExtraInitrdGrubPath)
                ? string.Empty
                : " " + boot.ExtraInitrdGrubPath;

            string partuuid = NormalizePartitionUuid(request.IsoHostPartitionUuid);

            return $@"menuentry ""Instalar {request.DistroName} (staging LinuxHub)"" {{
    insmod part_gpt
    insmod part_msdos
    insmod ntfs
    insmod loopback
    insmod iso9660
    set gfxpayload=keep
    set isofile=""{request.IsoGrubPath}""
    search --no-floppy --file --set=root $isofile
    loopback loop $isofile
    linux (loop)/{InstallDirectory}/boot/x86_64/vmlinuz-linux archisobasedir={InstallDirectory} img_dev=PARTUUID={partuuid} img_loop=$isofile{installerParameters}
    initrd (loop)/{InstallDirectory}/boot/x86_64/initramfs-linux.img{extraInitrd}
}}
";
        }

        /// <summary>O Windows reporta o GUID entre chaves e em maiúsculas; o <c>mount</c> do
        /// Linux resolve <c>PARTUUID=</c> por <c>/dev/disk/by-partuuid</c>, onde os nomes são
        /// minúsculos e sem chaves.</summary>
        private static string NormalizePartitionUuid(string partitionUuid) =>
            partitionUuid.Trim().Trim('{', '}').ToLowerInvariant();
    }
}
