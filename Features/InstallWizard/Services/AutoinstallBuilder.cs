using System.Text;
using System.Text.RegularExpressions;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Monta o <c>user-data</c> do cloud-init com a seção <c>autoinstall</c> que o subiquity
    /// consome — o que substitui as telas que o usuário preenchia à mão no instalador do
    /// Ubuntu. Lógica pura de texto, sem I/O, como <see cref="GrubConfigBuilder"/>.
    ///
    /// Só vira instalação desatendida de verdade em conjunto com o parâmetro
    /// <c>autoinstall</c> na linha de comando do kernel: sem ele o subiquity encontra este
    /// arquivo e ainda assim para para pedir confirmação. Ver <see cref="GrubConfigBuilder"/>.
    /// </summary>
    public static class AutoinstallBuilder
    {
        /// <summary>Nomes de usuário do Linux: minúsculas, começando por letra ou
        /// sublinhado. Um nome inválido aqui só apareceria como falha no meio da
        /// instalação, depois do ponto de não-retorno.</summary>
        private static readonly Regex UsernamePattern = new("^[a-z_][a-z0-9_-]{0,31}$", RegexOptions.Compiled);

        private static readonly Regex HostnamePattern =
            new("^[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$", RegexOptions.Compiled);

        public static string BuildUserData(
            InstallerConfig config,
            DiskLayout disk,
            string passwordHash,
            int seedPartitionNumber,
            StagingPartition? staging)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(disk);
            ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

            if (!UsernamePattern.IsMatch(config.Username))
            {
                throw new InvalidOperationException(
                    $"'{config.Username}' não é um nome de usuário válido no Linux. Use apenas " +
                    "letras minúsculas, números, hífen e sublinhado, começando por letra.");
            }

            if (!HostnamePattern.IsMatch(config.Hostname))
            {
                throw new InvalidOperationException(
                    $"'{config.Hostname}' não é um nome de máquina válido. Use apenas letras, " +
                    "números e hífen, sem começar nem terminar com hífen.");
            }

            bool isUefi = string.Equals(config.BootMode, "uefi", StringComparison.OrdinalIgnoreCase);
            InstallMode mode = string.Equals(config.InstallMode, "replace", StringComparison.OrdinalIgnoreCase)
                ? InstallMode.Replace
                : InstallMode.DualBoot;

            // Staging só existe no substituir: sem ela o clear-holders do curtin tenta soltar
            // a partição que hospeda a ISO. No dual-boot a ISO mora no Windows preservado.
            if (mode == InstallMode.Replace && staging is null)
            {
                throw new InvalidOperationException(
                    "O modo substituir precisa da partição de instalação com a ISO antes de " +
                    "gerar o autoinstall — sem ela o instalador apagaria a mídia de onde está rodando.");
            }

            var yaml = new StringBuilder();
            yaml.AppendLine("#cloud-config");
            yaml.AppendLine("autoinstall:");
            yaml.AppendLine("  version: 1");

            // Sem isto o instalador tenta se auto-atualizar pela rede antes de começar, o que
            // numa máquina sem rede configurada trava a instalação numa tela de espera —
            // exatamente o oposto de desatendida.
            yaml.AppendLine("  refresh-installer:");
            yaml.AppendLine("    update: false");

            // Precisa vir ANTES de `storage:` na ordem de leitura conceitual, mas o subiquity
            // não exige ordem entre chaves do topo — o que importa é que early-commands roda
            // antes da probe de storage em tempo de execução (ver EarlyCommandsBuilder). Fica
            // ausente quando o disco foi identificado só pelo critério de tamanho: não há nada
            // para resolver em tempo de execução nesse caso.
            string? earlyCommands = AutoinstallStorageBuilder.BuildEarlyCommands(disk, seedPartitionNumber, indentSpaces: 4);
            if (earlyCommands is not null)
            {
                yaml.AppendLine("  early-commands:");
                yaml.Append(earlyCommands);
            }

            yaml.AppendLine($"  locale: {Quote(config.Locale)}");
            yaml.AppendLine("  keyboard:");
            yaml.AppendLine($"    layout: {Quote(config.Keymap)}");
            yaml.AppendLine($"  timezone: {Quote(config.Timezone)}");

            yaml.AppendLine("  identity:");
            yaml.AppendLine($"    realname: {Quote(config.Username)}");
            yaml.AppendLine($"    username: {Quote(config.Username)}");
            yaml.AppendLine($"    hostname: {Quote(config.Hostname)}");
            yaml.AppendLine($"    password: {Quote(passwordHash)}");

            yaml.AppendLine("  ssh:");
            yaml.AppendLine("    install-server: false");

            yaml.AppendLine("  storage:");
            yaml.Append(AutoinstallStorageBuilder.Build(
                disk, mode, isUefi, indentSpaces: 4, seedPartitionNumber, staging?.PartitionNumber));

            // Devolve o espaço temporário ao usuário — mas só no primeiro boot do sistema
            // instalado. No substituir isso inclui a staging (a sessão live ainda lê a ISO
            // dela); no dual-boot só a semente.
            string? seedUuid = disk.Partitions
                .FirstOrDefault(p => p.Number == seedPartitionNumber)?.Guid;

            if (!string.IsNullOrWhiteSpace(seedUuid))
            {
                yaml.AppendLine("  late-commands:");
                yaml.Append(PostInstallCleanupBuilder.Build(staging?.PartitionUuid, seedUuid, indentSpaces: 4));
            }

            // Nada de chave `swap:` no topo — ela NÃO existe no autoinstall. Numa instalação
            // real o subiquity respondeu "Unrecognized top-level key 'swap'" e avisou que
            // versões futuras vão transformar isso em erro. O subiquity já cria um
            // /swap.img dimensionado sozinho quando não há partição de swap declarada, que é
            // exatamente o comportamento desejado — daí não haver substituto aqui.

            yaml.AppendLine("  shutdown: reboot");

            // O cloud-init é um parser Unix: o arquivo precisa sair com LF puro, senão o
            // '\r' entra no valor (mesma armadilha documentada em GrubConfigBuilder).
            return yaml.ToString().Replace("\r\n", "\n");
        }

        /// <summary>
        /// O <c>meta-data</c> pode ser mínimo, mas precisa EXISTIR: sem ele o cloud-init não
        /// reconhece a fonte NoCloud e ignora o <c>user-data</c> ao lado.
        /// </summary>
        public static string BuildMetaData(string instanceId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

            return $"instance-id: {instanceId}\n";
        }

        private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    }
}
