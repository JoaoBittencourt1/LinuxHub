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
        /// <summary>Folga sobre o tamanho da ISO: metadados do NTFS mais o alinhamento de
        /// 1 MiB do <c>New-Partition</c>. Uma partição do tamanho exato da ISO não a comporta.</summary>
        private const long SlackBytes = 512L * 1024 * 1024;

        internal const string VolumeLabel = "LHSTAGING";
        internal const string IsoFileName = "linuxhub.iso";

        /// <summary>Caminho que o <c>search --file</c> do GRUB procura. A ISO fica na raiz da
        /// partição de staging, e o GRUB compara caminhos relativos à raiz de cada volume —
        /// não conhece letra de unidade.</summary>
        public const string IsoGrubPath = "/" + IsoFileName;
        private const string SuccessMarker = "STAGING_OK:";
        private const string CopyMarker = "COPY_OK:";

        private readonly IIsoFileInfoProvider _isoFileInfo;

        public StagingPartitionService(IIsoFileInfoProvider isoFileInfo)
        {
            _isoFileInfo = isoFileInfo ?? throw new ArgumentNullException(nameof(isoFileInfo));
        }

        public long RequiredBytesFor(long isoSizeInBytes) => isoSizeInBytes + SlackBytes;

        public StagingPartition Create(int diskIndex, long isoSizeInBytes)
        {
            string output = ElevatedPowerShellRunner.Run(
                BuildCreateScript(diskIndex, RequiredBytesFor(isoSizeInBytes)),
                $"criação da partição de instalação no disco {diskIndex}");

            return ParseCreateOutputOrThrow(diskIndex, output);
        }

        public void CopyIso(StagingPartition partition, string isoSourcePath, IProgress<string>? progress)
        {
            ArgumentNullException.ThrowIfNull(partition);
            ArgumentException.ThrowIfNullOrWhiteSpace(isoSourcePath);

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

$guid = (Get-Partition -DiskNumber {diskIndex} -PartitionNumber $partition.PartitionNumber).Guid
if (-not $guid) {{ throw ""O Windows não informou o GUID da partição de instalação criada no disco {diskIndex}. Sem ele a partição não pode ser identificada com segurança depois."" }}

Write-Output ""{SuccessMarker} $($partition.PartitionNumber) $guid $letter""";

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
                $@"{SuccessMarker}\s*(\d+)\s+(\{{[0-9a-fA-F-]+\}}|[0-9a-fA-F-]{{36}})");

            if (!match.Success)
            {
                throw new InvalidOperationException(
                    "A partição de instalação foi criada, mas o Windows não informou o número e " +
                    "o identificador dela — sem esses dados a instalação não consegue continuar. " +
                    $"Saída recebida: {output}");
            }

            return new StagingPartition(
                diskIndex,
                int.Parse(match.Groups[1].Value),
                match.Groups[2].Value.Trim().Trim('{', '}').ToUpperInvariant());
        }
    }
}
