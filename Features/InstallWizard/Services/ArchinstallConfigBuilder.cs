using System.Globalization;
using System.Text;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Gera o JSON de configuração do <c>archinstall</c> 4.4 — a única parte capaz de apagar o
    /// Windows se estiver errada, por isso vive isolada e é testada à parte do script que a
    /// carrega (<see cref="ArchinstallScriptBuilder"/>). Lógica pura de texto, sem I/O.
    ///
    /// O schema é da versão 4.4, lida do pacote que a ISO 2026.08.01 instala
    /// (<c>research-archinstall.md</c>). Ele muda entre releases: a configuração gerada aqui
    /// vale para a build declarada no catálogo, não para "o Arch".
    ///
    /// Dois valores NÃO são conhecíveis do lado do Windows e saem como marcadores, substituídos
    /// pelo script já dentro da sessão live: o caminho do disco e o da ESP. O
    /// <c>disk_config</c> do archinstall endereça partição por caminho de kernel
    /// (<c>/dev/nvme0n1p1</c>), e <c>obj_id</c> — apesar do nome — é um identificador interno
    /// dele, não um PARTUUID.
    /// </summary>
    public static class ArchinstallConfigBuilder
    {
        /// <summary>Substituído pelo caminho do disco resolvido na sessão live. Precisa ser
        /// único o bastante para não colidir com nenhum path real — mesmo critério do
        /// <see cref="EarlyCommandsBuilder.DiskPathPlaceholder"/>.</summary>
        public const string DiskPathPlaceholder = "__LINUXHUB_DISK_PATH__";

        public const string EspPathPlaceholder = "__LINUXHUB_ESP_PATH__";

        /// <summary>
        /// A ESP é montada em <c>/efi</c>, e não em <c>/boot</c>, de propósito. A ESP criada
        /// pelo Windows costuma ter 100 MB; com ela em <c>/boot</c>, o kernel e os dois
        /// initramfs do Arch (o normal e o fallback, que sozinho passa de 90 MB) iriam para
        /// dentro dela e não caberiam. Em <c>/efi</c>, só os poucos megabytes do GRUB vão para
        /// a ESP e o resto fica na raiz (design.md, decisão 4).
        ///
        /// O <c>_add_grub_bootloader</c> do archinstall usa este mountpoint como
        /// <c>--efi-directory</c>, e acrescenta <c>--boot-directory</c> quando ele difere de
        /// <c>/boot</c> — que é exatamente o comportamento desejado aqui.
        /// </summary>
        private const string EspMountpoint = "/efi";

        private const long MebiByte = 1024 * 1024;

        public static string Build(
            InstallerConfig config,
            DiskLayout disk,
            string passwordHash)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(disk);
            ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

            PartitionLayout esp = disk.EfiSystemPartition
                ?? throw new InvalidOperationException(
                    "O disco alvo não tem EFI System Partition, e a instalação do Arch por este " +
                    "mecanismo reaproveita a ESP existente em vez de criar uma.");

            (long gapOffset, long gapSize) = disk.FindLargestFreeGap();
            (long rootOffset, long rootSize) = AlignToMebiByte(gapOffset, gapSize);

            if (rootSize < AutoinstallStorageBuilder.MinimumRootSizeBytes)
            {
                throw new InvalidOperationException(
                    $"O espaço livre no disco ({rootSize / (1024d * 1024 * 1024):n1} GB) é menor " +
                    "que o mínimo para instalar o sistema. A instalação foi interrompida antes " +
                    "de qualquer escrita.");
            }

            var json = new StringBuilder();
            json.AppendLine("{");

            // `removable: false` é explícito porque o default do archinstall é TRUE: com ele, o
            // GRUB iria para \EFI\BOOT\BOOTX64.EFI — o caminho de fallback do firmware, que numa
            // máquina com Windows já está em uso. Omitir a chave não é neutro.
            json.AppendLine("    \"bootloader_config\": {");
            json.AppendLine("        \"bootloader\": \"Grub\",");
            json.AppendLine("        \"uki\": false,");
            json.AppendLine("        \"removable\": false");
            json.AppendLine("    },");

            json.AppendLine("    \"disk_config\": {");
            json.AppendLine("        \"config_type\": \"manual_partitioning\",");
            json.AppendLine("        \"device_modifications\": [");
            json.AppendLine("            {");
            json.AppendLine($"                \"device\": \"{DiskPathPlaceholder}\",");

            // A linha que separa "instalar ao lado" de "apagar o Windows". Com `wipe: true` o
            // archinstall recria a tabela de partições inteira; com false ele usa a existente e
            // só toca no que for declarado como criar.
            json.AppendLine("                \"wipe\": false,");
            json.AppendLine("                \"partitions\": [");

            // A ESP do Windows entra como `existing`: o `_format_partitions` do archinstall
            // filtra por create/modify antes de formatar ("don't touch existing partitions"),
            // então declará-la assim é o que impede a formatação dela. Sem `dev_path` o próprio
            // archinstall recusa a entrada — é ele que aponta a partição.
            // O obj_id NÃO carrega o PARTUUID da ESP de propósito: ele é opaco para o
            // archinstall (uma chave de hash interna) e pôr o identificador real ali daria a
            // impressão de que é ele quem aponta a partição. Quem aponta é o dev_path.
            json.AppendLine(BuildPartition(
                objId: NewObjectId(),
                status: "existing",
                devPath: $"\"{EspPathPlaceholder}\"",
                offsetBytes: esp.OffsetBytes,
                sizeBytes: esp.SizeBytes,
                fsType: "fat32",
                mountpoint: EspMountpoint,
                flags: "\"boot\", \"esp\"",
                isLast: false));

            json.AppendLine(BuildPartition(
                objId: NewObjectId(),
                status: "create",
                devPath: "null",
                offsetBytes: rootOffset,
                sizeBytes: rootSize,
                fsType: "ext4",
                mountpoint: "/",
                flags: string.Empty,
                isLast: true));

            json.AppendLine("                ]");
            json.AppendLine("            }");
            json.AppendLine("        ]");
            json.AppendLine("    },");

            json.AppendLine($"    \"hostname\": {Quote(config.Hostname)},");
            json.AppendLine("    \"kernels\": [\"linux\"],");

            json.AppendLine("    \"locale_config\": {");
            json.AppendLine($"        \"kb_layout\": {Quote(config.Keymap)},");
            json.AppendLine($"        \"sys_lang\": {Quote(config.Locale)},");
            json.AppendLine("        \"sys_enc\": \"UTF-8\"");
            json.AppendLine("    },");

            json.AppendLine("    \"ntp\": true,");

            string? profileConfig = BuildProfileConfig(config.DesktopEnvironment);
            if (profileConfig is not null)
                json.AppendLine(profileConfig);

            json.AppendLine($"    \"timezone\": {Quote(config.Timezone)},");

            // Senha como hash, não texto: mesmo caminho que o subiquity já usa (Sha512Crypt). O
            // archinstall aceita `!password` em texto puro por compatibilidade, mas isso deixaria
            // a senha legível no script gravado no disco do Windows.
            json.AppendLine("    \"users\": [");
            json.AppendLine("        {");
            json.AppendLine($"            \"username\": {Quote(config.Username)},");
            json.AppendLine($"            \"enc_password\": {Quote(passwordHash)},");
            json.AppendLine("            \"sudo\": true,");
            json.AppendLine("            \"groups\": []");
            json.AppendLine("        }");
            json.AppendLine("    ]");

            json.Append('}');

            // O JSON é lido por um parser Unix dentro da sessão live: sai com LF puro.
            return json.ToString().Replace("\r\n", "\n");
        }

        /// <summary>
        /// Sem ambiente escolhido não há <c>profile_config</c> nenhum — o Arch instala limpo,
        /// que é o comportamento correto e o padrão desta distro.
        /// </summary>
        private static string? BuildProfileConfig(string desktopEnvironment)
        {
            if (ArchinstallDesktopProfiles.Find(desktopEnvironment) is not { } profile)
                return null;

            var json = new StringBuilder();
            json.AppendLine("    \"profile_config\": {");
            json.AppendLine("        \"profile\": {");
            json.AppendLine($"            \"main\": \"{ArchinstallDesktopProfiles.DesktopProfileName}\",");
            json.AppendLine($"            \"details\": [{Quote(profile.ProfileName)}]");
            json.AppendLine("        },");

            // O greeter é o que faz a sessão gráfica subir sozinha no primeiro boot. Sem ele o
            // ambiente é instalado e a máquina liga num terminal.
            json.AppendLine($"        \"greeter\": {Quote(profile.Greeter)},");
            json.AppendLine("        \"gfx_driver\": null");
            json.Append("    },");

            return json.ToString().TrimEnd('\n', '\r');
        }

        private static string BuildPartition(
            string objId,
            string status,
            string devPath,
            long offsetBytes,
            long sizeBytes,
            string fsType,
            string mountpoint,
            string flags,
            bool isLast)
        {
            var json = new StringBuilder();
            json.AppendLine("                    {");
            json.AppendLine($"                        \"obj_id\": {Quote(objId)},");
            json.AppendLine($"                        \"status\": \"{status}\",");
            json.AppendLine("                        \"type\": \"primary\",");
            json.AppendLine($"                        \"dev_path\": {devPath},");
            json.AppendLine($"                        \"start\": {BuildSize(offsetBytes)},");
            json.AppendLine($"                        \"size\": {BuildSize(sizeBytes)},");
            json.AppendLine($"                        \"fs_type\": \"{fsType}\",");
            json.AppendLine($"                        \"mountpoint\": \"{mountpoint}\",");
            json.AppendLine("                        \"mount_options\": [],");
            json.AppendLine($"                        \"flags\": [{flags}]");
            json.Append(isLast ? "                    }" : "                    },");

            return json.ToString().TrimEnd('\n', '\r');
        }

        /// <summary>O tamanho em bytes puros. O <c>sector_size</c> é estrutural — o archinstall
        /// só o usa para converter de e para setores, o que não acontece com unidade
        /// <c>B</c>.</summary>
        private static string BuildSize(long bytes) =>
            $"{{ \"value\": {bytes.ToString(CultureInfo.InvariantCulture)}, \"unit\": \"B\", " +
            "\"sector_size\": { \"value\": 512, \"unit\": \"B\" } }";

        /// <summary>
        /// O archinstall recusa partição a criar cujo início ou tamanho não estejam alinhados a
        /// 1 MiB ("Partition is misaligned"). O início sobe e o fim desce, para o resultado
        /// caber dentro do vão livre em vez de invadir a partição seguinte.
        /// </summary>
        private static (long OffsetBytes, long SizeBytes) AlignToMebiByte(long offset, long size)
        {
            long alignedOffset = (offset + MebiByte - 1) / MebiByte * MebiByte;
            long end = (offset + size) / MebiByte * MebiByte;

            return (alignedOffset, Math.Max(0, end - alignedOffset));
        }

        /// <summary>
        /// <c>obj_id</c> é um identificador interno do archinstall — um <c>uuid4()</c> que ele
        /// usa como chave de hash dentro do próprio processo, comentado no código dele como
        /// "invisible attr". NÃO é PARTUUID e não identifica nada no disco; quem faz isso é o
        /// <c>dev_path</c>. Para a partição a criar, qualquer valor único serve.
        /// </summary>
        private static string NewObjectId() => Guid.NewGuid().ToString();

        private static string Quote(string value) =>
            "\"" + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                + "\"";
    }
}
