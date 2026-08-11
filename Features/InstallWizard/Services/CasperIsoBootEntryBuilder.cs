using System.Text;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Entrada de boot das ISOs Ubuntu e derivadas. O texto aqui veio do
    /// <see cref="GrubConfigBuilder"/> sem alteração nenhuma quando a montagem virou uma
    /// abstração — é o único caminho de instalação exercitado em boot real, e
    /// <c>GrubConfigCharacterizationTests</c> existe para provar que ele continua idêntico.
    /// </summary>
    public sealed class CasperIsoBootEntryBuilder : IIsoBootEntryBuilder
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

        public static CasperIsoBootEntryBuilder Instance { get; } = new();

        public LiveSessionFamily Family => LiveSessionFamily.Casper;

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
        /// Os parâmetros da instalação desatendida vão ANTES do <c>---</c>: o que vem depois do
        /// separador é destinado ao sistema instalado, não ao instalador.
        ///
        /// Numa instalação desatendida o <c>splash</c> também sai: com ninguém para clicar em
        /// nada, a tela gráfica só serve para esconder a mensagem de erro se algo falhar.
        /// </summary>
        public string Build(IsoBootEntryRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var boot = request.Unattended;

            string installerParameters = (string.IsNullOrEmpty(boot.KernelParameters)
                ? string.Empty
                : " " + boot.KernelParameters) + NoPromptParameter;
            string targetParameters = boot.IsUnattended ? "quiet" : "quiet splash";

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
    }
}
