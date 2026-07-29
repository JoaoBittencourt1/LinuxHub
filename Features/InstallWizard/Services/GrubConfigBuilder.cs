using System.Text;

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
        public static string BuildConfig(
            string distroName,
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
            sb.Append(BuildIsoBootEntry(distroName, isoWindowsPath, enableAutoinstall));

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
            string distroName, string isoWindowsPath, bool enableAutoinstall = false)
        {
            string isoPath = ToGrubPath(isoWindowsPath);
            string installerParameters = enableAutoinstall ? " autoinstall" : string.Empty;
            string targetParameters = enableAutoinstall ? "quiet" : "quiet splash";

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
    initrd (loop)/casper/initrd
}}
";
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
