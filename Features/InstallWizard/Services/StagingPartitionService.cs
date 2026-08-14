using System.Text.RegularExpressions;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Cria a partição NTFS que hospeda a ISO e copia a ISO para ela.
    ///
    /// NTFS não é preferência, é a única opção: o casper só monta
    /// <c>ext2|ext3|ext4|xfs|jfs|reiserfs|vfat|ntfs|iso9660|btrfs|udf</c> (lido de
    /// <c>scripts/casper-helpers</c>, <c>is_supported_fs</c>, no initrd da ISO do Ubuntu
    /// 24.04.4), o Windows só formata nativamente NTFS/FAT32/exFAT, exFAT está fora da lista
    /// do casper, e FAT32 tem teto de 4 GB por arquivo contra os 6,2 GB da ISO. Sobra NTFS.
    /// </summary>
    public sealed class StagingPartitionService : IStagingPartitionService
    {
        /// <summary>Folga sobre o tamanho da ISO, para metadados do NTFS. Uma partição do
        /// tamanho exato da ISO não a comporta.</summary>
        private const long SlackBytes = 512L * 1024 * 1024;

        /// <summary>Alinhamento que o Windows aplica a partição nova desde o Vista SP1.</summary>
        private const long AlignmentBytes = 1024 * 1024;

        internal const string VolumeLabel = "LHSTAGING";
        internal const string IsoFileName = "linuxhub.iso";

        /// <summary>Caminho que o <c>search --file</c> do GRUB procura. A ISO fica na raiz da
        /// partição de staging, e o GRUB compara caminhos relativos à raiz de cada volume —
        /// não conhece letra de unidade.</summary>
        public const string IsoGrubPath = "/" + IsoFileName;
        private const string SuccessMarker = "STAGING_OK:";
        private const string CopyMarker = "COPY_OK:";

        private readonly IIsoFileInfoProvider _isoFileInfo;
        private readonly IInstallationPlanMutationGuard _mutationGuard;

        public StagingPartitionService(
            IIsoFileInfoProvider isoFileInfo,
            IInstallationPlanMutationGuard mutationGuard)
        {
            _isoFileInfo = isoFileInfo ?? throw new ArgumentNullException(nameof(isoFileInfo));
            _mutationGuard = mutationGuard ?? throw new ArgumentNullException(nameof(mutationGuard));
        }

        /// <summary>
        /// Arredondado para cima até o próximo MiB. Sem isso a staging termina no meio de um
        /// limite de alinhamento e o Windows desperdiça o resto ao criar a partição SEGUINTE —
        /// que é a semente, cuja folga é exatamente 1 MiB. O vão sobrava com zero margem: no
        /// caso real de uma ISO de 6.655.619.072 bytes, a staging pedia 7.192.489.984, que não
        /// é múltiplo de MiB, e a semente ficava com 12 KB de sobra. Qualquer alinhamento a
        /// mais (se o Windows arredondar também o TAMANHO, e não só o início) fazia a semente
        /// não caber, com o mesmo "Not enough available capacity" que originou tudo isto.
        ///
        /// Alinhando aqui, o vão que sobra para a semente é exato e não depende de como o
        /// Windows trata o resto.
        /// </summary>
        public long RequiredBytesFor(long isoSizeInBytes) =>
            AlignUp(isoSizeInBytes + SlackBytes);

        private static long AlignUp(long value) =>
            (value + AlignmentBytes - 1) / AlignmentBytes * AlignmentBytes;

        public StagingPartition Create(int diskIndex, long isoSizeInBytes)
        {
            _mutationGuard.EnsurePublishedForDisk(diskIndex);
            string output = ElevatedPowerShellRunner.Run(
                BuildCreateScript(diskIndex, RequiredBytesFor(isoSizeInBytes)),
                $"criação da partição de instalação no disco {diskIndex}");

            return ParseCreateOutputOrThrow(diskIndex, output);
        }

        public void CopyIso(StagingPartition partition, string isoSourcePath, IProgress<string>? progress)
        {
            ArgumentNullException.ThrowIfNull(partition);
            ArgumentException.ThrowIfNullOrWhiteSpace(isoSourcePath);
            _mutationGuard.EnsurePublishedForDisk(partition.DiskIndex);

            long expectedSize = _isoFileInfo.GetSizeInBytes(isoSourcePath);

            ElevatedPowerShellRunner.Run(
                BuildCopyScript(partition, isoSourcePath, expectedSize),
                "cópia da ISO para a partição de instalação");
        }

        /// <summary>
        /// Devolve número E PARTUUID numa ida só. O PARTUUID é o que identifica a partição
        /// depois, do lado Linux e na limpeza pós-instalação — o número muda quando o
        /// instalador reescreve a tabela, então guardá-lo sozinho não serviria de nada.
        /// </summary>
        internal static string BuildCreateScript(int diskIndex, long sizeInBytes) => $@"
$ErrorActionPreference = 'Stop'

$partition = New-Partition -DiskNumber {diskIndex} -Size {sizeInBytes} -AssignDriveLetter
$letter = $partition.DriveLetter
if (-not $letter) {{ throw ""O Windows não atribuiu letra de unidade à partição de instalação criada no disco {diskIndex}."" }}

Format-Volume -DriveLetter $letter -FileSystem NTFS -NewFileSystemLabel '{VolumeLabel}' -Force -Confirm:$false | Out-Null

# Tira a letra: a particao nao e para o Explorador, e a copia da ISO remonta com
# Add-PartitionAccessPath. Deixar a letra aqui fazia esse passo falhar com
# Cannot assign multiple drive letters to a partition.
Remove-PartitionAccessPath -DiskNumber {diskIndex} -PartitionNumber $partition.PartitionNumber -AccessPath ""$letter`:\""

$guid = (Get-Partition -DiskNumber {diskIndex} -PartitionNumber $partition.PartitionNumber).Guid
if (-not $guid) {{ throw ""O Windows não informou o GUID da partição de instalação criada no disco {diskIndex}. Sem ele a partição não pode ser identificada com segurança depois."" }}

Write-Output ""{SuccessMarker} $($partition.PartitionNumber) $guid $($partition.Offset)""";

        /// <summary>
        /// Confere o tamanho depois de copiar. Uma cópia truncada (disco cheio, energia,
        /// I/O) produziria uma ISO que o GRUB até localiza mas o casper não consegue montar —
        /// e isso só apareceria depois do reboot, numa tela preta, sem log e sem como avisar.
        /// Falhar aqui é a última chance de falhar de forma visível.
        /// </summary>
        internal static string BuildCopyScript(StagingPartition partition, string isoSourcePath, long expectedSize) => $@"
$ErrorActionPreference = 'Stop'

Add-PartitionAccessPath -DiskNumber {partition.DiskIndex} -PartitionNumber {partition.PartitionNumber} -AssignDriveLetter
$letter = (Get-Partition -DiskNumber {partition.DiskIndex} -PartitionNumber {partition.PartitionNumber}).DriveLetter
if (-not $letter) {{ throw ""Não foi possível montar a partição de instalação ({partition.PartitionNumber}) do disco {partition.DiskIndex} para copiar a ISO."" }}

$destino = ""$letter`:\{IsoFileName}""
try {{
    Copy-Item -LiteralPath '{isoSourcePath}' -Destination $destino -Force

    $copiado = (Get-Item -LiteralPath $destino).Length
    if ($copiado -ne {expectedSize}) {{
        Remove-Item -LiteralPath $destino -Force -ErrorAction SilentlyContinue
        throw ""A cópia da ISO para a partição de instalação saiu incompleta ($copiado de {expectedSize} bytes). A instalação foi interrompida para não reiniciar com uma ISO corrompida.""
    }}
}} finally {{
    Remove-PartitionAccessPath -DiskNumber {partition.DiskIndex} -PartitionNumber {partition.PartitionNumber} -AccessPath ""$letter`:\"" -ErrorAction SilentlyContinue
}}

Write-Output ""{CopyMarker} {expectedSize}""";

        internal static StagingPartition ParseCreateOutputOrThrow(int diskIndex, string output)
        {
            Match match = Regex.Match(
                output ?? string.Empty,
                $@"{SuccessMarker}\s*(\d+)\s+(\{{[0-9a-fA-F-]+\}}|[0-9a-fA-F-]{{36}})\s+(\d+)");

            if (!match.Success)
            {
                throw new InvalidOperationException(
                    "A partição de instalação foi criada, mas o Windows não informou o número, " +
                    "o identificador e o offset dela — sem esses dados a instalação não consegue continuar. " +
                    $"Saída recebida: {output}");
            }

            return new StagingPartition(
                diskIndex,
                int.Parse(match.Groups[1].Value),
                match.Groups[2].Value.Trim().Trim('{', '}').ToUpperInvariant(),
                long.Parse(match.Groups[3].Value));
        }
    }
}
