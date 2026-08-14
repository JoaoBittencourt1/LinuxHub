namespace LinuxHub.Features.InstallWizard.Models
{
    public class InstallerConfig
    {
        // === Distro ===
        public string DistroId { get; set; } = string.Empty;
        public string DistroName { get; set; } = string.Empty;
        public string DistroFamily { get; set; } = string.Empty;
        public string DistroVersion { get; set; } = string.Empty;
        public string IsoPath { get; set; } = string.Empty;

        // === Install ===
        public string BootMode { get; set; } = string.Empty;      // uefi | bios
        public string InstallMode { get; set; } = string.Empty;   // replace | dualboot
        public int TargetDiskIndex { get; set; }
        public int? TargetPartitionIndex { get; set; }
        public int? EfiPartitionIndex { get; set; }

        public int LinuxPartitionSizeGb { get; set; }

        // === User ===
        public string Username { get; set; } = string.Empty;

        // Texto puro, não hash: o Windows não tem crypt(3) (glibc SHA-512-
        // crypt) disponível — o hash de senha real é gerado no lado Linux,
        // via `chpasswd` dentro do chroot (installer/core/lib/user.sh), que
        // usa o glibc do próprio sistema instalado. install.sh apaga
        // install.conf ao final para limitar a janela de exposição.
        public string Password { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;

        // === System ===
        public string Locale { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public string Keymap { get; set; } = string.Empty;

        /// <summary>Ambiente gráfico escolhido, quando o mecanismo permite escolher — vazio
        /// quando a ISO já embute um. Lido apenas pelo preparer do mecanismo que o suporta;
        /// há precedente de campo que não serve a todo caminho (<see cref="EfiPartitionIndex"/>),
        /// e um campo de dado ignorado por quem não o usa não é violação de OCP — o que seria
        /// violação é um <c>if</c> de identidade de distro (design.md, decisão 5).</summary>
        public string DesktopEnvironment { get; set; } = string.Empty;

        // === Swap ===
        public bool SwapEnabled { get; set; }
        public int SwapSizeGb { get; set; }
    }
}
