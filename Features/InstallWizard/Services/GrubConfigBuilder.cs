using System.Text;
using LinuxHub.Common.Models;
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
        /// <paramref name="isoEntryBuilder"/> monta a entrada da ISO conforme a família de
        /// sessão live da distro. Omiti-lo cai no casper, que é o padrão declarado em
        /// <see cref="LiveSessionFamily"/> e o comportamento que este gerador sempre teve —
        /// é o que mantém as chamadas antigas produzindo exatamente o mesmo arquivo.
        /// </summary>
        public static string BuildConfig(
            string distroName,
            string isoWindowsPath,
            bool includeWindowsChainload,
            UnattendedBootParameters? unattended = null,
            IIsoBootEntryBuilder? isoEntryBuilder = null,
            string isoHostPartitionUuid = "")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(distroName);
            ArgumentException.ThrowIfNullOrWhiteSpace(isoWindowsPath);

            var entryBuilder = isoEntryBuilder ?? CasperIsoBootEntryBuilder.Instance;

            var sb = new StringBuilder();
            sb.AppendLine("set timeout=10");
            sb.AppendLine("set default=0");
            sb.AppendLine();
            sb.Append(entryBuilder.Build(new IsoBootEntryRequest(
                distroName,
                ToGrubPath(isoWindowsPath),
                unattended ?? UnattendedBootParameters.Interactive,
                isoHostPartitionUuid)));

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
