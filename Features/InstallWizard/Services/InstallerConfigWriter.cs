using System.IO;
using System.Text;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed class InstallerConfigWriter : IInstallerConfigWriter
    {
        private static readonly string OutputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LinuxHub");

        private const string OutputFileName = "install.conf";

        public void Save(InstallerConfig cfg)
        {
            var sb = new StringBuilder();

            sb.AppendLine("# === Distro ===");
            sb.AppendLine($"DISTRO_ID={EscapeForBash(cfg.DistroId)}");
            sb.AppendLine($"DISTRO_NAME={EscapeForBash(cfg.DistroName)}");
            sb.AppendLine($"DISTRO_FAMILY={EscapeForBash(cfg.DistroFamily)}");
            sb.AppendLine($"DISTRO_VERSION={EscapeForBash(cfg.DistroVersion)}");
            sb.AppendLine($"ISO_PATH={EscapeForBash(cfg.IsoPath)}");

            sb.AppendLine();
            sb.AppendLine("# === Install ===");
            sb.AppendLine($"BOOT_MODE={EscapeForBash(cfg.BootMode)}");
            sb.AppendLine($"INSTALL_MODE={EscapeForBash(cfg.InstallMode)}");
            sb.AppendLine($"TARGET_DISK_INDEX={cfg.TargetDiskIndex}");

            if (cfg.TargetPartitionIndex.HasValue)
                sb.AppendLine($"TARGET_PARTITION_INDEX={cfg.TargetPartitionIndex}");

            if (cfg.EfiPartitionIndex.HasValue)
                sb.AppendLine($"EFI_PARTITION_INDEX={cfg.EfiPartitionIndex}");

            if (cfg.LinuxPartitionSizeGb > 0)
                sb.AppendLine($"LINUX_PARTITION_SIZE_GB={cfg.LinuxPartitionSizeGb}");

            sb.AppendLine();
            sb.AppendLine("# === User ===");
            // Texto puro, não hash — ver InstallerConfig.Password. install.sh apaga este
            // arquivo ao final da instalação para limitar a janela de exposição.
            // Todos os campos abaixo vêm de texto digitado pelo usuário e são escapados
            // com aspas simples (EscapeForBash) — este arquivo é lido via `source` direto
            // pelo install.sh; sem escape, uma senha com aspas/`$`/crase quebraria o
            // parse ou, na pior hipótese, executaria comando shell como root.
            sb.AppendLine($"USERNAME={EscapeForBash(cfg.Username)}");
            sb.AppendLine($"PASSWORD={EscapeForBash(cfg.Password)}");
            sb.AppendLine($"HOSTNAME={EscapeForBash(cfg.Hostname)}");

            sb.AppendLine();
            sb.AppendLine("# === System ===");
            sb.AppendLine($"LOCALE={EscapeForBash(cfg.Locale)}");
            sb.AppendLine($"KEYMAP={EscapeForBash(cfg.Keymap)}");
            sb.AppendLine($"TIMEZONE={EscapeForBash(cfg.Timezone)}");

            sb.AppendLine();
            sb.AppendLine("# === Swap ===");
            sb.AppendLine($"SWAP_ENABLED={(cfg.SwapEnabled ? "true" : "false")}");
            sb.AppendLine($"SWAP_SIZE_GB={cfg.SwapSizeGb}");

            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(OutputDirectory, OutputFileName), sb.ToString());
        }

        /// <summary>Envolve em aspas simples pro valor virar um literal bash seguro
        /// (nunca expandido/interpretado), escapando aspas simples embutidas na forma
        /// padrão POSIX (<c>'\''</c>) — necessário porque este arquivo é <c>source</c>ado
        /// direto como script bash, não só parseado como um .env/.ini.</summary>
        internal static string EscapeForBash(string value) =>
            "'" + value.Replace("'", "'\\''") + "'";
    }
}
