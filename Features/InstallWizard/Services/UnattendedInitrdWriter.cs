using System.IO;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Grava o cpio do preseed ao lado da ISO (dual-boot) ou na raiz da partição de staging
    /// (substituir) — ver <see cref="IUnattendedInitrdWriter"/> para por que o destino não é
    /// livre.
    /// </summary>
    public sealed class UnattendedInitrdWriter : IUnattendedInitrdWriter
    {
        internal const string FileName = "linuxhub-preseed.cpio";

        /// <summary>Caminho na partição de staging, onde o arquivo mora na raiz — o mesmo
        /// critério de <see cref="StagingPartitionService.IsoGrubPath"/>.</summary>
        internal const string StagingGrubPath = "/" + FileName;

        public string Write(byte[] archive, string isoWindowsPath, StagingPartition? staging)
        {
            ArgumentNullException.ThrowIfNull(archive);
            ArgumentException.ThrowIfNullOrWhiteSpace(isoWindowsPath);

            if (staging is not null)
            {
                ElevatedPowerShellRunner.Run(
                    BuildStagingWriteScript(staging, archive),
                    "gravação do preseed na partição de instalação");

                return StagingGrubPath;
            }

            // Dual-boot: mesmo diretório da ISO, então necessariamente o mesmo volume que o
            // search --file vai encontrar. Não exige elevação — é uma pasta onde o usuário já
            // gravou a própria ISO.
            string directory = Path.GetDirectoryName(isoWindowsPath)
                ?? throw new InvalidOperationException(
                    $"Não foi possível determinar a pasta da ISO a partir de '{isoWindowsPath}' " +
                    "para gravar o preseed ao lado dela.");

            string destination = Path.Combine(directory, FileName);
            File.WriteAllBytes(destination, archive);

            return GrubConfigBuilder.ToGrubPath(destination);
        }

        /// <summary>
        /// O cpio vai como hex e é remontado do outro lado: um here-string de PowerShell é
        /// texto, e este conteúdo é binário — qualquer passagem por string corromperia bytes
        /// que não formam um caractere válido. Para poucos KB o custo do hex é irrelevante.
        /// </summary>
        internal static string BuildStagingWriteScript(StagingPartition staging, byte[] archive) => $@"
$ErrorActionPreference = 'Stop'

Add-PartitionAccessPath -DiskNumber {staging.DiskIndex} -PartitionNumber {staging.PartitionNumber} -AssignDriveLetter
$letter = (Get-Partition -DiskNumber {staging.DiskIndex} -PartitionNumber {staging.PartitionNumber}).DriveLetter
if (-not $letter) {{ throw ""Não foi possível montar a partição de instalação ({staging.PartitionNumber}) do disco {staging.DiskIndex} para gravar o preseed."" }}

$destino = ""$letter`:\{FileName}""
try {{
    $bytes = [byte[]] -split ('{Convert.ToHexString(archive)}' -replace '..', '0x$& ')
    [System.IO.File]::WriteAllBytes($destino, $bytes)

    $gravado = (Get-Item -LiteralPath $destino).Length
    if ($gravado -ne {archive.Length}) {{
        throw ""O preseed foi gravado incompleto ($gravado de {archive.Length} bytes). A instalação foi interrompida para não reiniciar numa automação pela metade.""
    }}
}} finally {{
    Remove-PartitionAccessPath -DiskNumber {staging.DiskIndex} -PartitionNumber {staging.PartitionNumber} -AccessPath ""$letter`:\"" -ErrorAction SilentlyContinue
}}";
    }
}
