using System.Text.RegularExpressions;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>Identidade da partição que hospeda os arquivos da mídia live.</summary>
    public sealed record LiveMediaStagingPartition(
        int DiskIndex,
        int PartitionNumber,
        string PartitionUuid,
        long OffsetBytes,
        long SizeBytes);

    public interface ILiveMediaStagingService
    {
        long RequiredBytesFor(long liveMediaIsoSizeBytes);
        LiveMediaStagingPartition Create(int diskIndex, long liveMediaIsoSizeBytes);
        void CopyLiveFiles(LiveMediaStagingPartition partition, string liveMediaIsoWindowsPath);
    }

    /// <summary>
    /// Prepara a mídia live para bootar: cria uma partição FAT32 e copia para dentro dela o
    /// CONTEÚDO da ISO (<c>live/vmlinuz</c>, <c>live/initrd.img</c>,
    /// <c>live/filesystem.squashfs</c>), em vez de deixar a ISO como arquivo e pedir ao GRUB
    /// que a monte em loopback.
    ///
    /// A diferença não é de estilo, é de quantos pontos de falha existem entre o firmware e o
    /// instalador. Com a ISO na NTFS, o <c>live-boot</c> precisava, de dentro do initramfs:
    /// varrer dispositivos, montar NTFS por FUSE, localizar o arquivo, criar um dispositivo de
    /// laço, montar iso9660 e só então achar o squashfs. Cada etapa é uma forma de o boot
    /// morrer sem chegar a lugar nenhum — e morreu, várias vezes, sempre com a mesma tela preta.
    ///
    /// Com os arquivos numa partição FAT32 o GRUB carrega o kernel direto (<c>fat</c> é módulo
    /// nativo, sem FUSE, sem hibernação, sem Fast Startup) e o <c>live-boot</c> encontra o
    /// squashfs pelo caminho normal. Some a cadeia inteira.
    /// </summary>
    public sealed class LiveMediaStagingService : ILiveMediaStagingService
    {
        /// <summary>Rótulo do volume. Não é usado para localizar a partição no boot — o GRUB
        /// pré-compilado embarca <c>search_fs_file</c>, não <c>search_label</c> — mas torna a
        /// partição reconhecível para quem inspecionar o disco.</summary>
        internal const string VolumeLabel = "LHLIVE";

        /// <summary>Folga sobre o tamanho da ISO. O que é copiado é o conteúdo dela (menor que
        /// a própria ISO), então isto é generoso de propósito: uma partição apertada falharia
        /// no meio da cópia, depois de já ter mexido na tabela de partição.</summary>
        private const long SlackBytes = 512L * 1024 * 1024;

        private const long AlignmentBytes = 1024 * 1024;
        private const string SuccessMarker = "LIVEMEDIA_OK:";
        private const string CopyMarker = "LIVECOPY_OK:";

        private readonly IInstallationPlanMutationGuard _mutationGuard;

        public LiveMediaStagingService(IInstallationPlanMutationGuard mutationGuard)
        {
            _mutationGuard = mutationGuard ?? throw new ArgumentNullException(nameof(mutationGuard));
        }

        public long RequiredBytesFor(long liveMediaIsoSizeBytes) =>
            AlignUp(liveMediaIsoSizeBytes + SlackBytes);

        private static long AlignUp(long value) =>
            (value + AlignmentBytes - 1) / AlignmentBytes * AlignmentBytes;

        public LiveMediaStagingPartition Create(int diskIndex, long liveMediaIsoSizeBytes)
        {
            _mutationGuard.EnsurePublishedForDisk(diskIndex);

            string output = ElevatedPowerShellRunner.Run(
                BuildCreateScript(diskIndex, RequiredBytesFor(liveMediaIsoSizeBytes)),
                $"criação da partição da mídia live no disco {diskIndex}");

            return ParseCreateOutputOrThrow(diskIndex, output);
        }

        public void CopyLiveFiles(LiveMediaStagingPartition partition, string liveMediaIsoWindowsPath)
        {
            ArgumentNullException.ThrowIfNull(partition);
            ArgumentException.ThrowIfNullOrWhiteSpace(liveMediaIsoWindowsPath);
            _mutationGuard.EnsurePublishedForDisk(partition.DiskIndex);

            ElevatedPowerShellRunner.Run(
                BuildCopyScript(partition, liveMediaIsoWindowsPath),
                "cópia dos arquivos da mídia live para a partição de boot");
        }

        internal static string BuildCreateScript(int diskIndex, long sizeInBytes) => $@"
$ErrorActionPreference = 'Stop'

$partition = New-Partition -DiskNumber {diskIndex} -Size {sizeInBytes} -AssignDriveLetter
$letter = $partition.DriveLetter
if (-not $letter) {{ throw ""O Windows não atribuiu letra de unidade à partição da mídia live no disco {diskIndex}."" }}

# FAT32, não NTFS: o GRUB lê FAT nativamente e o initramfs também, sem FUSE.
Format-Volume -DriveLetter $letter -FileSystem FAT32 -NewFileSystemLabel '{VolumeLabel}' -Force -Confirm:$false | Out-Null

$guid = (Get-Partition -DiskNumber {diskIndex} -PartitionNumber $partition.PartitionNumber).Guid
if (-not $guid) {{ throw ""O Windows não informou o GUID da partição da mídia live criada no disco {diskIndex}."" }}

# A letra sai aqui; a cópia remonta com Add-PartitionAccessPath. Deixá-la faria
# o passo seguinte falhar com 'Cannot assign multiple drive letters'.
Remove-PartitionAccessPath -DiskNumber {diskIndex} -PartitionNumber $partition.PartitionNumber -AccessPath ""$letter`:\""

Write-Output ""{SuccessMarker} $($partition.PartitionNumber) $guid $($partition.Offset) $($partition.Size)""";

        /// <summary>
        /// Monta a ISO com o próprio Windows e copia <c>live\</c> para a partição. Só esses
        /// três arquivos importam: o GRUB que boota é o que já está na ESP, então nada de
        /// <c>EFI\</c> precisa vir junto — e nem viria, porque numa ISO gerada por
        /// <c>grub-mkrescue</c> a cadeia EFI mora dentro de uma imagem El Torito, invisível na
        /// árvore ISO9660.
        /// </summary>
        internal static string BuildCopyScript(
            LiveMediaStagingPartition partition, string liveMediaIsoWindowsPath) => $@"
$ErrorActionPreference = 'Stop'

Add-PartitionAccessPath -DiskNumber {partition.DiskIndex} -PartitionNumber {partition.PartitionNumber} -AssignDriveLetter
$letter = (Get-Partition -DiskNumber {partition.DiskIndex} -PartitionNumber {partition.PartitionNumber}).DriveLetter
if (-not $letter) {{ throw ""Não foi possível montar a partição da mídia live ({partition.PartitionNumber}) do disco {partition.DiskIndex}."" }}

$image = $null
try {{
    $image = Mount-DiskImage -ImagePath '{liveMediaIsoWindowsPath}' -PassThru -ErrorAction Stop
    $isoLetter = ($image | Get-Volume).DriveLetter
    if (-not $isoLetter) {{ throw ""O Windows montou a mídia live mas não atribuiu letra de unidade a ela."" }}

    Copy-Item -LiteralPath ""$isoLetter`:\live"" -Destination ""$letter`:\"" -Recurse -Force

    # O initramfs procura estes nomes exatos. O Windows expõe nomes ISO9660 em
    # maiúsculas em alguns casos, e FAT preserva o que recebe — conferir aqui é
    # a diferença entre falhar agora, com mensagem, e falhar no boot, mudo.
    foreach ($required in @('live\vmlinuz', 'live\initrd.img', 'live\filesystem.squashfs')) {{
        $full = Join-Path ""$letter`:\"" $required
        if (-not (Test-Path -LiteralPath $full)) {{
            throw ""A cópia da mídia live não produziu $required — o boot não encontraria o sistema.""
        }}
        if ((Get-Item -LiteralPath $full).Length -le 0) {{
            throw ""A cópia da mídia live produziu $required vazio.""
        }}
    }}

    $copied = (Get-Item -LiteralPath ""$letter`:\live\filesystem.squashfs"").Length
    Write-Output ""{CopyMarker} $copied""
}} finally {{
    if ($image) {{ Dismount-DiskImage -ImagePath '{liveMediaIsoWindowsPath}' -ErrorAction SilentlyContinue | Out-Null }}
    Remove-PartitionAccessPath -DiskNumber {partition.DiskIndex} -PartitionNumber {partition.PartitionNumber} -AccessPath ""$letter`:\"" -ErrorAction SilentlyContinue
}}";

        internal static LiveMediaStagingPartition ParseCreateOutputOrThrow(int diskIndex, string output)
        {
            Match match = Regex.Match(
                output ?? string.Empty,
                $@"{SuccessMarker}\s*(\d+)\s+(\{{[0-9a-fA-F-]+\}}|[0-9a-fA-F-]{{36}})\s+(\d+)\s+(\d+)");

            if (!match.Success)
            {
                throw new InvalidOperationException(
                    "A partição da mídia live foi criada, mas o Windows não informou número, " +
                    "identificador, offset e tamanho dela. Saída recebida: " + output);
            }

            return new LiveMediaStagingPartition(
                DiskIndex: diskIndex,
                PartitionNumber: int.Parse(match.Groups[1].Value),
                PartitionUuid: match.Groups[2].Value.Trim('{', '}'),
                OffsetBytes: long.Parse(match.Groups[3].Value),
                SizeBytes: long.Parse(match.Groups[4].Value));
        }
    }
}
