using System.Text;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Gera o bloco <c>late-commands:</c> que instala, no sistema recém-instalado, um serviço
    /// de execução única que apaga a partição de staging e a semente no primeiro boot.
    ///
    /// Por que só no primeiro boot: durante a instalação a sessão live está LENDO a ISO da
    /// staging — apagá-la mata o instalador junto. E <c>late-commands</c> também roda dentro da
    /// sessão live, então também não serve. O único momento em que ninguém depende mais dessas
    /// partições é depois do reboot, já no sistema instalado.
    ///
    /// Este é o código mais perigoso do projeto: roda como root, no sistema do usuário, e
    /// apaga partições. As defesas, em ordem:
    /// <list type="number">
    /// <item>Identifica por PARTUUID, nunca por índice — o instalador reescreve a tabela e os
    /// números mudam.</item>
    /// <item>Confere rótulo E tipo de filesystem antes de apagar. Os dois têm que bater; um só
    /// não basta, porque rótulo é copiável e tipo de filesystem é comum.</item>
    /// <item>Qualquer divergência aborta aquela partição, sem tocar nela.</item>
    /// <item>Nunca apaga uma partição montada — se estiver montada, alguém depende dela e a
    /// premissa "ninguém mais precisa disso" está errada.</item>
    /// </list>
    /// </summary>
    public static class PostInstallCleanupBuilder
    {
        private const string ServiceName = "linuxhub-cleanup";
        private const string ScriptPath = "/usr/local/sbin/linuxhub-cleanup.sh";
        private const string UnitPath = "/etc/systemd/system/linuxhub-cleanup.service";

        /// <summary>
        /// <paramref name="stagingPartitionUuid"/> é o PARTUUID da staging no modo substituir,
        /// ou <c>null</c> no dual-boot (sem staging). <paramref name="seedPartitionUuid"/> é
        /// sempre a semente. Ambos vêm normalizados para minúsculo porque é assim que o
        /// <c>blkid</c> os reporta.
        /// </summary>
        public static string Build(
            string? stagingPartitionUuid,
            string seedPartitionUuid,
            int indentSpaces)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(seedPartitionUuid);

            string indent = new(' ', indentSpaces);
            string item = indent + "  ";

            var yaml = new StringBuilder();
            yaml.AppendLine($"{indent}- |");

            foreach (string line in BuildLateCommand(stagingPartitionUuid, seedPartitionUuid)
                         .Replace("\r\n", "\n").Split('\n'))
            {
                yaml.AppendLine($"{item}{line}");
            }

            return yaml.ToString();
        }

        /// <summary>
        /// O script vai para o alvo em base64 e é decodificado lá. Escrevê-lo inline com
        /// here-doc dentro de um <c>curtin in-target</c> exigiria escapar <c>$</c> duas vezes
        /// (uma para o YAML, outra para o shell), e cada nível de escape é uma chance de o
        /// script chegar corrompido — num script que apaga partição, isso é inaceitável.
        /// </summary>
        internal static string BuildLateCommand(string? stagingPartitionUuid, string seedPartitionUuid)
        {
            string script = BuildCleanupScript(stagingPartitionUuid, seedPartitionUuid);
            string unit = BuildUnitFile();

            string scriptBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
            string unitBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(unit));

            var command = new StringBuilder();
            command.AppendLine("set -e");
            command.AppendLine($"echo '{scriptBase64}' | base64 -d > /target{ScriptPath}");
            command.AppendLine($"chmod 700 /target{ScriptPath}");
            command.AppendLine($"echo '{unitBase64}' | base64 -d > /target{UnitPath}");
            command.Append($"curtin in-target -- systemctl enable {ServiceName}.service");

            return command.ToString();
        }

        /// <summary>
        /// <c>Before=</c> nada e <c>WantedBy=multi-user.target</c>: a limpeza não é urgente e
        /// não pode atrasar o boot. <c>RemainAfterExit</c> é irrelevante aqui porque o serviço
        /// se desabilita — mas a desabilitação vive no script, não na unit, para que ela também
        /// aconteça quando o script aborta por divergência (a partição fica, o serviço não
        /// tenta de novo a cada boot).
        /// </summary>
        internal static string BuildUnitFile() =>
            """
            [Unit]
            Description=LinuxHub: remove a area temporaria usada durante a instalacao
            After=multi-user.target

            [Service]
            Type=oneshot
            ExecStart=/usr/local/sbin/linuxhub-cleanup.sh

            [Install]
            WantedBy=multi-user.target
            """.Replace("\r\n", "\n") + "\n";

        internal static string BuildCleanupScript(string? stagingPartitionUuid, string seedPartitionUuid)
        {
            string stagingRemover = string.IsNullOrWhiteSpace(stagingPartitionUuid)
                ? string.Empty
                : $$"""
            remover "{{Normalize(stagingPartitionUuid)}}" "{{StagingPartitionService.VolumeLabel}}" "ntfs"

            """;

            return $$"""
            #!/bin/sh
            # Gerado pelo LinuxHub. Remove a area temporaria da instalacao (staging no modo
            # substituir; semente do cloud-init nos dois modos), devolvendo o espaco ao usuario.
            #
            # Roda uma vez so: a ultima linha desabilita o proprio servico, inclusive quando
            # alguma particao foi recusada — repetir a cada boot nao mudaria o resultado.

            remover() {
                uuid="$1"
                rotulo_esperado="$2"
                fs_esperado="$3"

                dev=$(blkid -t PARTUUID="$uuid" -o device 2>/dev/null) || return 0
                [ -n "$dev" ] || return 0

                # Rotulo E tipo de filesystem precisam bater. Um so nao basta: rotulo qualquer um
                # copia, e tipo de filesystem e comum demais. Divergiu, nao e a particao que
                # criamos — sai sem tocar nela.
                rotulo=$(blkid -o value -s LABEL "$dev" 2>/dev/null)
                fs=$(blkid -o value -s TYPE "$dev" 2>/dev/null)
                [ "$rotulo" = "$rotulo_esperado" ] || return 0
                [ "$fs" = "$fs_esperado" ] || return 0

                # Montada significa que alguem depende dela, e a premissa deste script e que
                # ninguem mais precisa dessas particoes.
                if grep -q "^$dev " /proc/mounts; then
                    return 0
                fi

                disco="/dev/$(lsblk -no pkname "$dev")"
                numero=$(cat "/sys/class/block/$(basename "$dev")/partition")
                [ -n "$numero" ] || return 0

                sfdisk --delete "$disco" "$numero" || return 0
            }

            {{stagingRemover}}remover "{{Normalize(seedPartitionUuid)}}" "{{CloudInitSeedWriter.VolumeLabel}}" "vfat"

            systemctl disable {{ServiceName}}.service >/dev/null 2>&1 || true
            rm -f {{UnitPath}} {{ScriptPath}}
            """.Replace("\r\n", "\n") + "\n";
        }

        private static string Normalize(string value) => value.Trim().Trim('{', '}').ToLowerInvariant();
    }
}
