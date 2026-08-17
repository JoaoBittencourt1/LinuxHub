using System.Text;
using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Gera o grub.cfg de staging que boota a ISO via loopback (spec boot-staging —
    /// "Bootar a ISO da distro via loopback"). Lógica pura de texto, sem I/O.
    /// Usa <c>search --file</c> para localizar a ISO e o bootmgr do Windows em vez de
    /// numeração de disco/partição assumida — mesmo princípio de D3 (design.md): nunca
    /// um índice fixo, sempre uma busca real. Ver design.md, Open Questions, sobre a
    /// decisão de não reaproveitar o motor de boot do Ventoy.
    /// </summary>
    public static class GrubConfigBuilder
    {
        /// <summary>
        /// Desliga o "Please remove the installation medium, then press ENTER" no fim da
        /// instalação. Não é frescura de mensagem: o <c>/sbin/casper-stop</c> (lido do pacote
        /// casper 1.498, o da ISO do Ubuntu 24.04.4) imprime a mensagem e então BLOQUEIA num
        /// <c>read x &lt; /dev/console</c> esperando ENTER — é por isso que o PC não reiniciava
        /// sozinho ao terminar. O trecho que este parâmetro desarma:
        /// <code>
        /// prompt=1
        /// if grep -qs noprompt /proc/cmdline || [ -e /run/casper-no-prompt ]; then prompt=; fi
        /// </code>
        /// Com <c>prompt</c> vazio o script devolve antes do <c>read</c>, e a tentativa de
        /// <c>eject</c> que vem no meio (a origem do erro de cdrom numa máquina sem cdrom
        /// nenhum) já sai silenciada pelo redirecionamento do próprio casper.
        ///
        /// Vale nos dois modos: com ou sem autoinstall não existe mídia para remover, porque a
        /// ISO é um arquivo no disco interno. Vai antes do <c>---</c> por ser lido do
        /// <c>/proc/cmdline</c> da sessão live, não do sistema instalado.
        ///
        /// Só este, e não <c>find_iso=</c> (que faria o casper-stop sair logo no topo): aquele
        /// parâmetro troca o mecanismo de localização da ISO que já funciona aqui.
        /// </summary>
        private const string NoPromptParameter = " noprompt";


        public static string BuildConfig(
            string distroName,
            LiveBootSystem liveBoot,
            string isoWindowsPath,
            bool includeWindowsChainload,
            bool enableAutoinstall = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(distroName);
            ArgumentException.ThrowIfNullOrWhiteSpace(isoWindowsPath);

            var sb = new StringBuilder();
            sb.AppendLine("set timeout=10");
            sb.AppendLine("set default=0");
            sb.AppendLine();
            sb.Append(BuildIsoBootEntry(distroName, liveBoot, isoWindowsPath, enableAutoinstall));

            if (includeWindowsChainload)
            {
                sb.AppendLine();
                sb.Append(BuildWindowsChainloadEntry());
            }

            // GRUB é um parser de herança Unix — grub.cfg precisa de fim de linha \n puro.
            // AppendLine() usa Environment.NewLine (\r\n no Windows, onde este código
            // sempre roda); sem essa normalização, o arquivo sai com CRLF misturado com LF
            // (dependendo de como os literais multi-linha do .cs foram salvos em disco),
            // o que pode fazer o GRUB falhar o parse ou deixar um '\r' fantasma no fim de
            // um valor (ex.: o caminho da ISO em $isofile).
            return sb.ToString().Replace("\r\n", "\n");
        }

        /// <summary>
        /// A linha do kernel segue o <c>/boot/grub/loopback.cfg</c> que a própria ISO do
        /// Ubuntu 24.04 traz — é a receita do fornecedor para exatamente este caso (bootar a
        /// ISO a partir de um arquivo, e não de mídia removível):
        /// <code>linux /casper/vmlinuz iso-scan/filename=${iso_path} --- quiet splash</code>
        /// Divergir dela sem motivo só acrescenta variáveis a um cenário que já é difícil de
        /// depurar depois do reboot. O que acrescentamos ao que vem de lá:
        /// <list type="bullet">
        /// <item><c>boot=casper</c> — redundante no Ubuntu (o initrd traz
        /// <c>conf.d/default-boot-to-casper.conf</c>, que assume <c>casper</c> quando o
        /// parâmetro não vem), mas necessário em derivadas que constroem o initrd sem esse
        /// default.</item>
        /// <item>o <c>search</c>/<c>loopback</c> — no fluxo do fornecedor quem monta o loop é
        /// o grub.cfg que inclui o loopback.cfg; aqui não há esse invólucro.</item>
        /// </list>
        ///
        /// <paramref name="enableAutoinstall"/> acrescenta o parâmetro <c>autoinstall</c>. Ele
        /// é o que torna a instalação de fato desatendida: com o <c>user-data</c> presente na
        /// partição CIDATA mas SEM este parâmetro, o subiquity acha o arquivo e ainda assim
        /// para numa tela pedindo confirmação — que é o comportamento padrão de segurança
        /// dele, não um bug. Ele vai ANTES do <c>---</c>: o que vem depois do separador é
        /// destinado ao sistema instalado, não ao instalador.
        ///
        /// Nesse modo o <c>splash</c> também sai: numa instalação em que ninguém vai clicar em
        /// nada, a tela gráfica só serve para esconder a mensagem de erro se algo falhar.
        /// </summary>
        internal static string BuildIsoBootEntry(
            string distroName,
            LiveBootSystem liveBoot,
            string isoWindowsPath,
            bool enableAutoinstall = false)
        {
            string isoPath = ToGrubPath(isoWindowsPath);

            var recipe = liveBoot switch
            {
                LiveBootSystem.Casper => CasperRecipe(enableAutoinstall),
                LiveBootSystem.Archiso => ArchisoRecipe(),
                _ => throw new NotSupportedException(
                    $"Não há receita de boot validada para {distroName}. Gerar um grub.cfg " +
                    "assumindo o layout de outra distro produz uma entrada que não boota, " +
                    "depois do disco já ter sido alterado."),
            };

            return $@"menuentry ""Instalar {distroName} (staging LinuxHub)"" {{
    insmod part_gpt
    insmod part_msdos
    insmod ntfs
    insmod loopback
    insmod iso9660
    insmod probe
    set gfxpayload=keep
    set isofile=""{isoPath}""
    search --no-floppy --file --set=root $isofile
{recipe.SetupLines}    loopback loop $isofile
    {recipe.KernelLine}
    {recipe.InitrdLine}
}}
";
        }

        /// <summary>As linhas que mudam de um sistema live para o outro. O resto do
        /// menuentry (insmod, search, loopback) é igual para todos.</summary>
        private sealed record BootRecipe(string SetupLines, string KernelLine, string InitrdLine);

        private static BootRecipe CasperRecipe(bool enableAutoinstall)
        {
            string installerParameters =
                (enableAutoinstall ? " autoinstall" : string.Empty) + NoPromptParameter;
            string targetParameters = enableAutoinstall ? "quiet" : "quiet splash";

            return new BootRecipe(
                SetupLines: string.Empty,
                KernelLine: $"linux (loop)/casper/vmlinuz boot=casper iso-scan/filename=$isofile{installerParameters} --- {targetParameters}",
                InitrdLine: "initrd (loop)/casper/initrd");
        }

        /// <summary>
        /// Segue o <c>/boot/grub/loopback.cfg</c> que a própria ISO do Arch traz — a receita
        /// do fornecedor para bootar a imagem a partir de um arquivo. Nada aqui é comum com
        /// o casper: não há <c>iso-scan/filename</c>, não há <c>boot=</c>, e o separador
        /// <c>---</c> não se aplica.
        ///
        /// O <c>probe</c> é o ponto central. O hook <c>archiso_loop_mnt</c> monta a partição
        /// que hospeda a ISO (<c>img_dev</c>) e só então abre o loopback no arquivo dentro
        /// dela (<c>img_loop</c>, caminho relativo à raiz dessa partição). Ou seja, o kernel
        /// precisa saber identificar a partição do Windows — e é o GRUB que lê o UUID do
        /// filesystem real em tempo de boot, em vez de nós deduzirmos do lado do Windows como
        /// o Linux vai nomear aquele disco. O hook aceita a forma <c>UUID=</c> literal:
        /// <code>case "${dev}" in 'UUID='* | 'LABEL='* | ...) : ;; *) dev="${resolved_dev}" ;; esac</code>
        ///
        /// <c>copytoram=y</c> não é otimização: o mesmo hook só solta a partição hospedeira
        /// nesse modo —
        /// <code>if [ "${copytoram}" = "y" ]; then losetup -d "${_dev_loop}"; umount /run/archiso/img_dev; fi</code>
        /// Sem isso o Windows fica montado enquanto o instalador roda, e reparticionar o
        /// disco que segura a própria ISO falha. O custo é precisar de RAM para o airootfs
        /// (~1 GB nesta ISO) — deixar no <c>auto</c> padrão tornaria isso dependente da
        /// memória livre do momento, que é a diferença entre instalar e não instalar.
        /// </summary>
        private static BootRecipe ArchisoRecipe() =>
            new(SetupLines: "    probe --set=isodevuuid --fs-uuid $root\n",
                KernelLine: "linux (loop)/arch/boot/x86_64/vmlinuz-linux archisobasedir=arch img_dev=UUID=$isodevuuid img_loop=$isofile copytoram=y",
                InitrdLine: "initrd (loop)/arch/boot/x86_64/initramfs-linux.img");

        internal static string BuildWindowsChainloadEntry() => @"menuentry ""Windows"" {
    insmod part_msdos
    insmod ntfs
    search --no-floppy --file --set=root /bootmgr
    chainloader +1
}
";

        /// <summary>
        /// Converte um caminho absoluto do Windows (<c>C:\Users\...\ubuntu.iso</c>) no
        /// caminho unix-style que o GRUB usa dentro do volume localizado por
        /// <c>search --file</c> (<c>/Users/.../ubuntu.iso</c>) — GRUB não conhece letras
        /// de unidade, só caminhos relativos à raiz do volume.
        /// </summary>
        internal static string ToGrubPath(string windowsAbsolutePath)
        {
            string path = windowsAbsolutePath.Replace('\\', '/');

            int colon = path.IndexOf(':');
            if (colon >= 0)
                path = path[(colon + 1)..];

            return path.StartsWith('/') ? path : "/" + path;
        }
    }
}
