using System.Text;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Gera o bloco <c>early-commands:</c> do autoinstall — o único ponto em que o autoinstall
    /// é reescrito EM TEMPO DE EXECUÇÃO, depois do boot do instalador e antes da probe de
    /// storage do subiquity. Isso é comportamento documentado do subiquity, não um workaround:
    /// <c>early-commands</c> roda "before probing for block and network devices", e
    /// <c>/autoinstall.yaml</c> é relido do disco depois dele rodar, exatamente para permitir
    /// esse tipo de reescrita.
    ///
    /// Existe porque o <c>match:</c> do subiquity é declarativo e só compara atributos de disco
    /// já conhecidos — ele não sabe responder "qual disco tem uma partição com este PARTUUID"
    /// ou "qual disco tem esta assinatura MBR". Resolver isso exige ler a tabela de partições
    /// em tempo de execução, o que este script faz via <c>blkid</c>, substituindo
    /// <see cref="DiskPathPlaceholder"/> — usado por
    /// <see cref="AutoinstallStorageBuilder.BuildDiskMatch"/> no lugar do path real — pelo
    /// caminho do disco físico resolvido, antes do curtin ler o arquivo.
    ///
    /// Usa <c>path:</c>, não <c>serial:</c> — descoberto numa instalação real que morreu com
    /// "matched no disk" mesmo com o disco certo resolvido: <c>lsblk -dno serial</c> e o probe
    /// interno do subiquity nem sempre derivam o serial do mesmo jeito (a mesma classe de
    /// divergência que já existia entre Windows e Linux, agora entre duas ferramentas Linux).
    /// O <c>path</c> não sofre disso porque é o MESMO valor, resolvido e consumido dentro do
    /// mesmo boot, sem outra ferramenta reinterpretando-o — nenhuma leitura adicional entre a
    /// resolução e o uso.
    /// </summary>
    public static class EarlyCommandsBuilder
    {
        /// <summary>Marcador que aparece no <c>match:</c> gerado por
        /// <see cref="AutoinstallStorageBuilder"/> no lugar do path real. Só o script gerado
        /// aqui sabe substituí-lo — por isso ele precisa ser único o bastante para não colidir
        /// com nenhum path de disco de verdade.</summary>
        public const string DiskPathPlaceholder = "__LINUXHUB_DISK_PATH__";

        /// <summary>Script para resolver o disco por PARTUUID (GPT) — o GUID pertence à
        /// partição semente, então primeiro acha a partição, depois o disco pai dela.</summary>
        public static string BuildForPartuuid(string partitionGuid, int indentSpaces) =>
            Build(
                $"partition=$(blkid -t PARTUUID=\"{Normalize(partitionGuid)}\" -o device)\n" +
                "disk=\"/dev/$(lsblk -no pkname \"$partition\")\"",
                indentSpaces);

        /// <summary>Script para resolver o disco por assinatura MBR — a assinatura pertence ao
        /// disco inteiro (não a uma partição), então o <c>blkid</c> já aponta direto para
        /// ele.</summary>
        public static string BuildForMbrSignature(string diskSignatureHex, int indentSpaces) =>
            Build(
                $"disk=$(blkid -t PTUUID=\"{Normalize(diskSignatureHex)}\" -o device)",
                indentSpaces);

        private static string Build(string diskResolutionCommands, int indentSpaces)
        {
            var script = new StringBuilder();
            script.AppendLine("set -e");
            script.AppendLine(diskResolutionCommands);
            // Delimitador `|`, não `/`: $disk é um path (`/dev/sda`) e conteria o delimitador
            // padrão do sed, quebrando o comando.
            script.Append($"sed -i \"s|{DiskPathPlaceholder}|$disk|\" /autoinstall.yaml");

            string indent = new(' ', indentSpaces);
            string item = indent + "  ";

            var yaml = new StringBuilder();
            yaml.AppendLine($"{indent}- |");
            foreach (string line in script.ToString().Replace("\r\n", "\n").Split('\n'))
                yaml.AppendLine($"{item}{line}");

            return yaml.ToString();
        }

        private static string Normalize(string value) => value.Trim().Trim('{', '}').ToLowerInvariant();
    }
}
