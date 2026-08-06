using System.Text;
using LinuxHub.Features.InstallWizard.Models;

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
            string isoWindowsPath,
            bool includeWindowsChainload,
            UnattendedBootParameters? unattended = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(distroName);
            ArgumentException.ThrowIfNullOrWhiteSpace(isoWindowsPath);

            var sb = new StringBuilder();
            sb.AppendLine("set timeout=10");
            sb.AppendLine("set default=0");
            sb.AppendLine();
            sb.Append(BuildIsoBootEntry(distroName, isoWindowsPath, unattended));

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
        /// <paramref name="unattended"/> traz, já resolvido pelo preparer do mecanismo em uso,
        /// o que a instalação desatendida precisa acrescentar — esta classe não sabe (nem deve
        /// saber) que subiquity usa <c>autoinstall</c> e Ubiquity usa <c>automatic-ubiquity</c>
        /// (design.md, D4). Os parâmetros vão ANTES do <c>---</c>: o que vem depois do
        /// separador é destinado ao sistema instalado, não ao instalador.
        ///
        /// Numa instalação desatendida o <c>splash</c> também sai: com ninguém para clicar em
        /// nada, a tela gráfica só serve para esconder a mensagem de erro se algo falhar.
        /// </summary>
        internal static string BuildIsoBootEntry(
            string distroName, string isoWindowsPath, UnattendedBootParameters? unattended = null)
        {
            var boot = unattended ?? UnattendedBootParameters.Interactive;

            string isoPath = ToGrubPath(isoWindowsPath);
            string installerParameters = (string.IsNullOrEmpty(boot.KernelParameters)
                ? string.Empty
                : " " + boot.KernelParameters) + NoPromptParameter;
            string targetParameters = boot.IsUnattended ? "quiet" : "quiet splash";

            return $@"menuentry ""Instalar {distroName} (staging LinuxHub)"" {{
    insmod part_gpt
    insmod part_msdos
    insmod ntfs
    insmod loopback
    insmod iso9660
    set gfxpayload=keep
    set isofile=""{isoPath}""
    search --no-floppy --file --set=root $isofile
    loopback loop $isofile
    linux (loop)/casper/vmlinuz boot=casper iso-scan/filename=$isofile{installerParameters} --- {targetParameters}
{BuildInitrdStanza(boot.ExtraInitrdGrubPath)}
}}
";
        }

        /// <summary>
        /// O nome do initrd dentro de <c>/casper</c> varia por distro/versão — <c>initrd</c>
        /// sem extensão no Ubuntu 24.04.4 (a receita original deste arquivo), mas
        /// <c>initrd.lz</c> no Linux Mint 22.3 (confirmado abrindo o <c>grub.cfg</c> real da
        /// ISO). Hardcodar um nome só bootava kernel sem o hook do casper embutido — GRUB
        /// carregava silenciosamente um initrd vazio/inexistente e o kernel caía em
        /// "VFS: Unable to mount root fs on unknown-block(0,0)" (bug real, reportado testando
        /// Mint). Em vez de adivinhar do lado do Windows — o que exigiria abrir a ISO fora
        /// desta classe, que é texto puro sem I/O — deixamos o próprio GRUB testar em tempo de
        /// boot, igual ao <c>search --file</c> já usado acima para achar a ISO: nunca um nome
        /// fixo, sempre uma checagem real contra o que está de fato dentro do loopback.
        ///
        /// <paramref name="extraInitrdGrubPath"/> é carregado como um segundo arquivo no mesmo
        /// comando <c>initrd</c>. O kernel concatena todos os cpio informados num initramfs só
        /// — é assim que o preseed do Ubiquity vira o <c>/preseed.cfg</c> que o casper procura
        /// na raiz, sem precisar escrever dentro da ISO (que é read-only) nem depender de rede
        /// (design.md, D1). Vai DEPOIS do initrd da ISO: entre arquivos com o mesmo caminho,
        /// vence o último, então o nosso é quem tem que sobrepor.
        /// </summary>
        private static string BuildInitrdStanza(string? extraInitrdGrubPath)
        {
            string[] candidates = ["initrd.lz", "initrd.img", "initrd.gz", "initrd"];
            string extra = string.IsNullOrWhiteSpace(extraInitrdGrubPath)
                ? string.Empty
                : " " + extraInitrdGrubPath;

            var sb = new StringBuilder();
            for (int i = 0; i < candidates.Length; i++)
            {
                string keyword = i == 0 ? "if" : "elif";
                sb.AppendLine($"    {keyword} [ -f (loop)/casper/{candidates[i]} ]; then");
                sb.AppendLine($"        initrd (loop)/casper/{candidates[i]}{extra}");
            }
            sb.AppendLine("    fi");

            return sb.ToString().TrimEnd('\n', '\r');
        }

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
