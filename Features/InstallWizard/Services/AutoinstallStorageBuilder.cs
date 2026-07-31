using System.Globalization;
using System.Text;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Gera a seção <c>storage:</c> do autoinstall — a única parte do YAML capaz de apagar
    /// o Windows se estiver errada, por isso vive isolada e testada à parte do resto do
    /// documento (<see cref="AutoinstallBuilder"/>).
    ///
    /// Princípio: no modo dual-boot, TODA partição já existente é declarada com
    /// <c>preserve: true</c>, com offset e tamanho reais lidos do disco E com o tipo de
    /// partição original. Uma partição existente que ficasse de fora da lista, ou com número
    /// divergente, faria o curtin tratá-la como espaço disponível. Declarar offset além do
    /// tamanho é redundante de propósito: se o disco tiver mudado entre o planejamento e o
    /// boot, o curtin aborta em vez de escrever no lugar errado.
    /// </summary>
    public static class AutoinstallStorageBuilder
    {
        private const string DiskId = "disk-linuxhub";

        /// <summary>Tipo das partições que o próprio instalador cria. Também é o padrão que o
        /// <c>sfdisk</c> aplica quando ninguém diz o tipo — ver
        /// <see cref="PartitionTypeOf"/>.</summary>
        private const string LinuxFilesystemGptType = "0FC63DAF-8483-4772-8E79-3D69D8477DE4";

        private const string LinuxFilesystemMbrType = "0x83";

        /// <summary>Tipo GPT da EFI System Partition, no formato que o <c>partition_type</c> do
        /// curtin espera (GUID maiúsculo, sem chaves).</summary>
        private const string EfiSystemPartitionGptType = "C12A7328-F81F-11D2-BA4B-00A0C93EC93B";

        /// <summary>Tamanho da ESP criada quando o disco alvo não tem nenhuma. 512 MiB é o que
        /// o próprio instalador do Ubuntu reserva — sobra para os kernels de várias distros
        /// sem custar espaço relevante do que o usuário destinou ao Linux.</summary>
        public const long NewEspSizeBytes = 512L * 1024 * 1024;

        /// <summary>
        /// Tamanho mínimo aceitável para a partição raiz. Abaixo disso o instalador do
        /// Ubuntu falha no meio, depois do ponto de não-retorno.
        /// </summary>
        public const long MinimumRootSizeBytes = 12L * 1024 * 1024 * 1024;

        public static string Build(
            DiskLayout disk,
            InstallMode mode,
            bool isUefi,
            int indentSpaces,
            int seedPartitionNumber,
            int? stagingPartitionNumber)
        {
            ArgumentNullException.ThrowIfNull(disk);

            if (mode == InstallMode.Replace && stagingPartitionNumber is null)
            {
                throw new InvalidOperationException(
                    "O modo substituir precisa do número da partição de instalação (staging) " +
                    "para declará-la como preservada — sem isso o instalador apagaria a ISO.");
            }

            return BuildExplicitConfig(
                disk, mode, isUefi, indentSpaces, seedPartitionNumber, stagingPartitionNumber);
        }

        /// <summary>
        /// Como identificar, do lado Linux, o disco que o Windows escolheu.
        ///
        /// NÃO usa o número de série do Windows como veio do WMI. Num NVMe o Windows reporta o
        /// EUI-64 do namespace (<c>0000_0000_0000_0000_6D1C_0035_0218_7C70.</c>, derivado do
        /// <c>UniqueId</c> <c>eui.…</c>) enquanto o Linux expõe em <c>serial</c> o número de
        /// série do CONTROLADOR — campos diferentes do mesmo dispositivo, que nunca coincidem.
        /// Isso custou uma instalação real, que morreu em
        /// <c>Filesystem/apply_autoinstall_config</c> com "matched no disk".
        ///
        /// Em vez disso, a identidade vem de dado de TABELA DE PARTIÇÃO — GUID da partição
        /// semente em GPT, assinatura de disco em MBR — que o Windows escreve e o Linux lê com
        /// o parser dele mesmo, sem tradução de driver/fabricante no meio. Como o `match:` do
        /// subiquity não sabe ler tabela de partições (só compara valores já conhecidos), a
        /// resolução PARTUUID/assinatura → disco acontece em tempo de execução via
        /// `early-commands` (<see cref="BuildEarlyCommands"/>), que substitui
        /// <see cref="EarlyCommandsBuilder.DiskPathPlaceholder"/> pelo path real do disco
        /// resolvido antes do curtin ler o arquivo. Usa <c>path:</c>, não <c>serial:</c> —
        /// ver <see cref="EarlyCommandsBuilder"/> para o motivo (achado numa instalação real).
        ///
        /// Só cai no critério de tamanho (`size: largest`/`smallest`) quando nenhum
        /// identificador de tabela de partição está disponível com segurança — disco MBR sem
        /// assinatura confiável, por exemplo. Nesse último recurso, fica de fora o disco do
        /// meio (3+ unidades) e o empate de tamanho no extremo: aí não existe critério correto
        /// disponível, e um match errado apaga o disco errado — então o caso é recusado, nunca
        /// chutado.
        ///
        /// Somar <c>model</c> como filtro extra pareceria mais seguro e é o oposto disso: se
        /// a string do Linux divergir da do Windows em um caractere, um match que funcionava
        /// vira "matched no disk".
        /// </summary>
        internal static string BuildDiskMatch(DiskLayout disk, int seedPartitionNumber) =>
            ResolveDiskIdentity(disk, seedPartitionNumber).MatchYaml;

        /// <summary>Bloco <c>early-commands:</c> a inserir no nível raiz do autoinstall (fora
        /// de <c>storage:</c>), ou <c>null</c> quando o disco foi identificado pelo critério de
        /// tamanho, caso em que não há nada para resolver em tempo de execução.</summary>
        internal static string? BuildEarlyCommands(DiskLayout disk, int seedPartitionNumber, int indentSpaces) =>
            ResolveDiskIdentity(disk, seedPartitionNumber).EarlyCommandsYaml;

        private readonly record struct DiskIdentity(string MatchYaml, string? EarlyCommandsYaml);

        private static DiskIdentity ResolveDiskIdentity(DiskLayout disk, int seedPartitionNumber, int earlyCommandsIndentSpaces = 4)
        {
            if (disk.IsGpt)
            {
                string guid = disk.Partitions
                    .FirstOrDefault(p => p.Number == seedPartitionNumber)?.Guid
                    .Trim().Trim('{', '}') ?? string.Empty;

                if (guid.Length > 0)
                {
                    return new DiskIdentity(
                        $"path: {EarlyCommandsBuilder.DiskPathPlaceholder}",
                        EarlyCommandsBuilder.BuildForPartuuid(guid, earlyCommandsIndentSpaces));
                }
            }
            else if (disk.DiskSignatureHex.Length > 0 && disk.HasUniqueDiskSignature)
            {
                return new DiskIdentity(
                    $"path: {EarlyCommandsBuilder.DiskPathPlaceholder}",
                    EarlyCommandsBuilder.BuildForMbrSignature(disk.DiskSignatureHex, earlyCommandsIndentSpaces));
            }

            if (disk.IsLargestDisk)
                return new DiskIdentity("size: largest", null);

            if (disk.IsSmallestDisk)
                return new DiskIdentity("size: smallest", null);

            throw new InvalidOperationException(
                $"A instalação automática não consegue identificar o disco {disk.Index} com " +
                "segurança do lado do Linux. Ela só sabe apontar o maior ou o menor disco da " +
                "máquina, e este não é nenhum dos dois (ou empata em tamanho com outro). " +
                "Escolher errado apagaria o disco errado. Escolha o maior ou o menor disco, " +
                "desconecte os demais, ou use a instalação manual.");
        }

        /// <summary>
        /// Um caminho só para os dois modos. A diferença entre substituir e dual-boot passou a
        /// ser apenas QUAIS partições entram na lista: no dual-boot todas as existentes são
        /// declaradas com <c>preserve: true</c>; no substituir, as do Windows são omitidas — e
        /// o curtin trata o que não está declarado como espaço disponível.
        ///
        /// O modo substituir NÃO usa mais <c>layout: name: direct</c>. Aquele layout reescreve
        /// o disco inteiro, e o <c>clear-holders</c> do curtin então precisa liberar TODAS as
        /// partições — inclusive a que hospeda a ISO, montada em <c>/isodevice</c> pelo casper
        /// e nunca solta. Ele falha, e a instalação aborta:
        /// <c>FAIL: removing previous storage devices</c> → <c>CurtinInstallError</c> (erro
        /// real, 2026-07-29). Declarando explicitamente, a staging fica preservada e o
        /// clear-holders só precisa soltar o Windows, que ninguém segura.
        /// </summary>
        private static string BuildExplicitConfig(
            DiskLayout disk,
            InstallMode mode,
            bool isUefi,
            int indentSpaces,
            int seedPartitionNumber,
            int? stagingPartitionNumber)
        {
            bool isReplace = mode == InstallMode.Replace;

            if (!disk.IsGpt && isUefi)
            {
                throw new InvalidOperationException(
                    $"O disco {disk.Index} não usa tabela de partição GPT, mas a máquina bootou em " +
                    "modo UEFI. Instalar nessa combinação exigiria converter a tabela de partição " +
                    "do disco, o que destruiria o conteúdo dele.");
            }

            // Instalar num disco que não é o do Windows é o caso normal em que o alvo não tem
            // ESP nenhuma — antes isso abortava a instalação. A ESP do disco de boot NÃO serve
            // de substituta aqui: usá-la faria o storage config descrever dois discos e mandaria
            // o curtin escrever dentro da partição de onde o Windows boota. O sistema novo ganha
            // a própria ESP, no disco dele.
            PartitionLayout? esp = disk.EfiSystemPartition;
            bool mustCreateEsp = isUefi && esp is null;

            // Quem sobrevive. No dual-boot é tudo; no substituir, só o que a instalação precisa
            // que continue existindo: a ESP (recebe o bootloader), a staging (hospeda a ISO em
            // uso — apagá-la mata a sessão live) e a semente (o cloud-init pode reler durante a
            // instalação). O resto some por omissão.
            HashSet<int> preserved = isReplace
                ? BuildReplacePreservedSet(esp, seedPartitionNumber, stagingPartitionNumber!.Value)
                : disk.Partitions.Select(p => p.Number).ToHashSet();

            (long gapOffset, long gapSize) = disk.FindLargestFreeGap(preserved);
            long requiredBytes = MinimumRootSizeBytes + (mustCreateEsp ? NewEspSizeBytes : 0);
            if (gapSize < requiredBytes)
            {
                throw new InvalidOperationException(
                    $"O maior espaço utilizável do disco {disk.Index} tem " +
                    $"{gapSize / (1024 * 1024 * 1024)} GB, abaixo do mínimo de " +
                    $"{requiredBytes / (1024 * 1024 * 1024)} GB para instalar o sistema. " +
                    "O encolhimento da partição do Windows não liberou o espaço esperado.");
            }

            string indent = new(' ', indentSpaces);
            string item = indent + "  ";
            string field = indent + "    ";

            var yaml = new StringBuilder();
            yaml.AppendLine($"{indent}config:");

            // O disco só é declarado; nunca reparticionado por inteiro. grub_device no disco
            // é o caminho de BIOS legado (o GRUB vai para o MBR); em UEFI quem recebe a flag
            // é a ESP, mais abaixo.
            yaml.AppendLine($"{item}- type: disk");
            yaml.AppendLine($"{field}id: {DiskId}");
            yaml.AppendLine($"{field}ptable: {(disk.IsGpt ? "gpt" : "msdos")}");
            yaml.AppendLine($"{field}match:");
            yaml.AppendLine($"{field}  {BuildDiskMatch(disk, seedPartitionNumber)}");
            yaml.AppendLine($"{field}preserve: true");
            yaml.AppendLine($"{field}grub_device: {Boolean(!isUefi)}");

            foreach (PartitionLayout partition in disk.Partitions
                         .Where(p => preserved.Contains(p.Number))
                         .OrderBy(p => p.Number))
            {
                yaml.AppendLine($"{item}- type: partition");
                yaml.AppendLine($"{field}id: {PartitionId(partition.Number)}");
                yaml.AppendLine($"{field}device: {DiskId}");
                yaml.AppendLine($"{field}number: {partition.Number}");
                yaml.AppendLine($"{field}offset: {partition.OffsetBytes}");
                yaml.AppendLine($"{field}size: {partition.SizeBytes}");
                yaml.AppendLine($"{field}preserve: true");
                yaml.AppendLine($"{field}partition_type: {PartitionTypeOf(disk, partition)}");

                if (partition.IsEfiSystemPartition)
                {
                    yaml.AppendLine($"{field}flag: boot");
                    yaml.AppendLine($"{field}grub_device: {Boolean(isUefi)}");
                }
                else if (!disk.IsGpt && partition.IsActive)
                {
                    // Num disco MBR a flag "ativa" é o que a BIOS procura para bootar. O
                    // curtin só a escreve quando a ação traz flag: boot — e essa flag, sozinha,
                    // também forçaria o tipo 0xEF; o partition_type acima é quem devolve o
                    // 0x07 do Windows (combinação documentada pelo curtin exatamente para isto).
                    yaml.AppendLine($"{field}flag: boot");
                }
            }

            int nextNumber = disk.NextFreePartitionNumber;
            long gapEnd = gapOffset + gapSize;
            long cursor = AlignUp(gapOffset);

            int? createdEspNumber = null;
            if (mustCreateEsp)
            {
                createdEspNumber = nextNumber++;

                yaml.AppendLine($"{item}- type: partition");
                yaml.AppendLine($"{field}id: {PartitionId(createdEspNumber.Value)}");
                yaml.AppendLine($"{field}device: {DiskId}");
                yaml.AppendLine($"{field}number: {createdEspNumber.Value}");
                yaml.AppendLine($"{field}offset: {cursor}");
                yaml.AppendLine($"{field}size: {NewEspSizeBytes}");
                yaml.AppendLine($"{field}partition_type: {EfiSystemPartitionGptType}");
                yaml.AppendLine($"{field}flag: boot");
                yaml.AppendLine($"{field}grub_device: true");
                yaml.AppendLine($"{field}wipe: superblock");

                cursor += NewEspSizeBytes;
            }

            int newNumber = nextNumber;
            long rootOffset = cursor;
            long rootSize = AlignDown(gapEnd - rootOffset);

            yaml.AppendLine($"{item}- type: partition");
            yaml.AppendLine($"{field}id: {PartitionId(newNumber)}");
            yaml.AppendLine($"{field}device: {DiskId}");
            yaml.AppendLine($"{field}number: {newNumber}");
            yaml.AppendLine($"{field}offset: {rootOffset}");
            yaml.AppendLine($"{field}size: {rootSize}");
            yaml.AppendLine($"{field}partition_type: " +
                (disk.IsGpt ? LinuxFilesystemGptType : Quote(LinuxFilesystemMbrType)));
            yaml.AppendLine($"{field}wipe: superblock");

            yaml.AppendLine($"{item}- type: format");
            yaml.AppendLine($"{field}id: format-root");
            yaml.AppendLine($"{field}volume: {PartitionId(newNumber)}");
            yaml.AppendLine($"{field}fstype: ext4");

            // Uma ESP que já existia é formatada COM preserve: true — sem isso o curtin a
            // reformata e leva junto o bootloader do Windows que mora nela. A que acabou de ser
            // criada é o oposto: precisa ser formatada de fato, porque ainda não tem filesystem.
            int? espNumber = esp?.Number ?? createdEspNumber;
            if (isUefi && espNumber is not null)
            {
                yaml.AppendLine($"{item}- type: format");
                yaml.AppendLine($"{field}id: format-esp");
                yaml.AppendLine($"{field}volume: {PartitionId(espNumber.Value)}");
                yaml.AppendLine($"{field}fstype: fat32");

                if (esp is not null)
                    yaml.AppendLine($"{field}preserve: true");
            }

            yaml.AppendLine($"{item}- type: mount");
            yaml.AppendLine($"{field}id: mount-root");
            yaml.AppendLine($"{field}device: format-root");
            yaml.AppendLine($"{field}path: /");

            if (isUefi && espNumber is not null)
            {
                yaml.AppendLine($"{item}- type: mount");
                yaml.AppendLine($"{field}id: mount-esp");
                yaml.AppendLine($"{field}device: format-esp");
                yaml.AppendLine($"{field}path: /boot/efi");
            }

            return yaml.ToString();
        }

        /// <summary>
        /// Tipo da partição no formato que o <c>partition_type</c> do curtin espera: GUID sem
        /// chaves em GPT, byte <c>0xNN</c> em MBR.
        ///
        /// Esta linha existe porque <c>preserve: true</c> NÃO preserva o tipo da partição. O
        /// curtin v2 (o caminho que o subiquity usa, e o único que respeita <c>offset</c>)
        /// monta a tabela inteira num script de <c>sfdisk</c>; para cada ação preservada,
        /// <c>PartTableEntry.preserve()</c> recupera do disco apenas <c>uuid</c>, <c>name</c> e
        /// <c>attrs</c> — <c>type</c> fica nulo, some do script, e o <c>sfdisk</c> escreve o
        /// padrão dele, "Linux filesystem". Numa instalação real isso carimbou
        /// <c>0FC63DAF-…</c> na partição reservada e no C: do Windows: os dados continuaram
        /// intactos, mas o Windows Boot Manager parou de reconhecer o volume como Windows e o
        /// boot passou a cair em "o Windows não iniciou corretamente". A ESP escapou por
        /// acidente, porque <c>flag: boot</c> mapeia para o GUID da EFI System Partition.
        ///
        /// Um tipo vazio aborta em vez de omitir o campo: omitir é exatamente o bug acima, e
        /// preferir "não sei montar o plano" a destruir o boot do Windows é a mesma regra que
        /// vale para o match do disco em <see cref="BuildDiskMatch"/>.
        /// </summary>
        internal static string PartitionTypeOf(DiskLayout disk, PartitionLayout partition)
        {
            if (disk.IsGpt)
            {
                string guid = partition.GptType.Trim().Trim('{', '}');
                if (guid.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"O Windows não informou o tipo GPT da partição {partition.Number} do disco " +
                        $"{disk.Index}. Sem esse dado a instalação reescreveria a partição como " +
                        "'Linux filesystem' e o Windows deixaria de bootar, então ela é recusada.");
                }

                return guid.ToUpperInvariant();
            }

            if (partition.MbrType == 0)
            {
                throw new InvalidOperationException(
                    $"O Windows não informou o tipo MBR da partição {partition.Number} do disco " +
                    $"{disk.Index}. Sem esse dado a instalação reescreveria a partição como " +
                    "'Linux' e o Windows deixaria de bootar, então ela é recusada.");
            }

            // Entre aspas porque `0x07` sem elas é um INTEIRO em YAML: o cloud-init entregaria
            // 7 ao curtin, e não a string "0x07" que o partition_type documenta.
            return Quote(string.Create(CultureInfo.InvariantCulture, $"0x{partition.MbrType:x2}"));
        }

        /// <summary>
        /// Só três partições sobrevivem ao modo substituir, e cada uma por um motivo distinto:
        /// a ESP porque é onde o bootloader do sistema novo vai morar (recriar não é problema,
        /// mas preservar a partição evita depender do curtin inventar uma); a staging porque a
        /// sessão live está LENDO a ISO dela neste instante — liberá-la é o que fazia o
        /// <c>clear-holders</c> falhar; e a semente porque o cloud-init pode reler o
        /// <c>user-data</c> durante a instalação.
        ///
        /// Tudo o mais — Windows, MSR, Recovery — fica de fora e vira espaço disponível.
        /// </summary>
        private static HashSet<int> BuildReplacePreservedSet(
            PartitionLayout? esp, int seedPartitionNumber, int stagingPartitionNumber)
        {
            var preserved = new HashSet<int> { seedPartitionNumber, stagingPartitionNumber };

            if (esp is not null)
                preserved.Add(esp.Number);

            return preserved;
        }

        private static string PartitionId(int number) =>
            string.Create(CultureInfo.InvariantCulture, $"partition-{number}");

        /// <summary>Alinhamento de 1 MiB — o mesmo que o Windows e o parted usam desde o
        /// Vista SP1. Uma partição desalinhada custa desempenho em SSD e faz algumas
        /// firmwares reclamarem.</summary>
        private const long AlignmentBytes = 1024 * 1024;

        private static long AlignUp(long value) =>
            (value + AlignmentBytes - 1) / AlignmentBytes * AlignmentBytes;

        private static long AlignDown(long value) => value / AlignmentBytes * AlignmentBytes;

        private static string Boolean(bool value) => value ? "true" : "false";

        internal static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    }
}
