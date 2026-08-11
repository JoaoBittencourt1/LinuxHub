using System.Text;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Gera o script que a sessão live do Arch executa sozinha, e que é quem de fato monta a
    /// configuração final do <c>archinstall</c>. Lógica pura de texto, sem I/O.
    ///
    /// Ele existe porque o <c>disk_config</c> endereça partições por caminho de kernel
    /// (<c>/dev/nvme0n1p1</c>) — um nome que só existe depois do boot e que o Windows não tem
    /// como saber. O app nomeia o alvo pelo PARTUUID, que vale nos dois lados, e a tradução
    /// para caminho acontece aqui, no único lugar onde ela é observável (design.md, decisão 9).
    ///
    /// Chega à sessão live pelo parâmetro de boot <c>script=</c>, lido pelo
    /// <c>.automated_script.sh</c> do perfil releng do archiso.
    /// </summary>
    public static class ArchinstallScriptBuilder
    {
        /// <summary>Nome do arquivo gravado ao lado da ISO — o mesmo lugar onde o cpio do
        /// Ubiquity já é gravado, e que o archiso monta em
        /// <see cref="ArchisoIsoBootEntryBuilder.HostPartitionMountPoint"/>.</summary>
        public const string FileName = "linuxhub-arch-install.sh";

        public static string Build(string archinstallConfigJson, string espPartitionGuid)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(archinstallConfigJson);
            ArgumentException.ThrowIfNullOrWhiteSpace(espPartitionGuid);

            string partuuid = NormalizeGuid(espPartitionGuid);

            var script = new StringBuilder();
            script.AppendLine("#!/usr/bin/env bash");
            script.AppendLine("# Gerado pelo LinuxHub. Executado pelo .automated_script.sh do archiso,");
            script.AppendLine("# via parâmetro de boot script=, no autologin de root da tty1.");
            script.AppendLine("set -euo pipefail");
            script.AppendLine();

            // O .automated_script.sh roda enquanto outros serviços ainda podem estar subindo — o
            // próprio archiso recomenda sincronizar aqui. Sem `|| true` o `set -e` derrubaria o
            // script quando algum serviço da sessão live estivesse apenas "degraded", o que não
            // impede a instalação.
            script.AppendLine("systemctl is-system-running --wait >/dev/null 2>&1 || true");
            script.AppendLine();

            script.AppendLine($"ESP_PARTUUID='{partuuid}'");
            script.AppendLine("ESP_PATH=\"$(blkid -t PARTUUID=\"$ESP_PARTUUID\" -o device || true)\"");
            script.AppendLine();

            // Falhar parando (§6.1). Um alvo que não resolve NÃO pode virar um palpite: o
            // archinstall aceita sem questionar qualquer /dev/... que exista, inclusive o de
            // outro disco. Sem a ESP resolvida, o script sai antes de chamar o instalador e a
            // máquina fica exatamente como estava.
            script.AppendLine("if [ -z \"$ESP_PATH\" ]; then");
            script.AppendLine("    echo 'LinuxHub: a particao EFI indicada nao foi encontrada nesta maquina.' >&2");
            script.AppendLine("    echo 'LinuxHub: a instalacao automatica foi cancelada; nenhum disco foi alterado.' >&2");
            script.AppendLine("    exit 1");
            script.AppendLine("fi");
            script.AppendLine();

            script.AppendLine("DISK_PATH=\"/dev/$(lsblk -no pkname \"$ESP_PATH\")\"");
            script.AppendLine("if [ \"$DISK_PATH\" = '/dev/' ] || [ ! -b \"$DISK_PATH\" ]; then");
            script.AppendLine("    echo 'LinuxHub: nao foi possivel determinar o disco da particao EFI.' >&2");
            script.AppendLine("    echo 'LinuxHub: a instalacao automatica foi cancelada; nenhum disco foi alterado.' >&2");
            script.AppendLine("    exit 1");
            script.AppendLine("fi");
            script.AppendLine();

            script.AppendLine("CONFIG=/tmp/linuxhub-archinstall.json");
            script.AppendLine("cat > \"$CONFIG\" <<'LINUXHUB_ARCHINSTALL_CONFIG'");
            script.AppendLine(archinstallConfigJson);
            script.AppendLine("LINUXHUB_ARCHINSTALL_CONFIG");
            script.AppendLine();

            // Delimitador `|`, não `/`: os valores substituídos são paths e conteriam o
            // delimitador padrão do sed (mesma armadilha já documentada no EarlyCommandsBuilder).
            script.AppendLine($"sed -i \"s|{ArchinstallConfigBuilder.DiskPathPlaceholder}|$DISK_PATH|g\" \"$CONFIG\"");
            script.AppendLine($"sed -i \"s|{ArchinstallConfigBuilder.EspPathPlaceholder}|$ESP_PATH|g\" \"$CONFIG\"");
            script.AppendLine();

            // Portão barato antes do caro: o --dry-run desserializa a configuração inteira,
            // valida bootloader contra layout e SAI antes de qualquer operação de disco. Pega
            // erro de schema e alvo que não resolve sem tocar num setor.
            //
            // Limite conhecido: uma falha de validação de bootloader é reportada pelo
            // archinstall com código de saída 0, então este portão pega a classe de erros que
            // levanta exceção (schema, modelo, dispositivo), não todas. O boot em VM continua
            // sendo a prova real.
            script.AppendLine("if ! archinstall --config \"$CONFIG\" --silent --dry-run; then");
            script.AppendLine("    echo 'LinuxHub: a configuracao gerada foi recusada pelo archinstall.' >&2");
            script.AppendLine("    echo 'LinuxHub: a instalacao automatica foi cancelada; nenhum disco foi alterado.' >&2");
            script.AppendLine("    exit 1");
            script.AppendLine("fi");
            script.AppendLine();

            script.AppendLine("archinstall --config \"$CONFIG\" --silent");

            // Script executado por um shell Unix: LF puro, sempre.
            return script.ToString().Replace("\r\n", "\n");
        }

        /// <summary>O Windows reporta o GUID entre chaves e em maiúsculas; o <c>blkid</c>
        /// compara PARTUUID em minúsculas e sem chaves.</summary>
        private static string NormalizeGuid(string guid) =>
            guid.Trim().Trim('{', '}').ToLowerInvariant();
    }
}
