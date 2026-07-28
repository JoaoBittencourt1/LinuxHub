using System.Text;
using System.Text.RegularExpressions;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Cria a partição que entrega o autoinstall ao cloud-init: FAT32, rótulo
    /// <c>CIDATA</c>, contendo <c>user-data</c> e <c>meta-data</c> na raiz. O cloud-init
    /// procura esse rótulo sozinho (fonte NoCloud), o que evita ter que passar
    /// <c>ds=nocloud;s=…</c> na linha de comando do kernel — parâmetro cuja sintaxe de
    /// escape do <c>;</c> varia entre versões do GRUB e do cloud-init e é uma fonte
    /// conhecida de instalação que boota e simplesmente ignora o arquivo.
    ///
    /// Nota de arquitetura: isto faz o lado Windows CRIAR uma partição, e não só encolher,
    /// contrariando o D1 do design.md ("o Windows só grava intenção"). É uma partição nova
    /// em espaço não alocado, nunca uma escrita sobre partição existente — mas continua
    /// sendo desvio de decisão registrada, não detalhe de implementação.
    /// </summary>
    public sealed class CloudInitSeedWriter : ICloudInitSeedWriter
    {
        /// <summary>Folgado para dois arquivos de texto, e acima do mínimo que o
        /// <c>Format-Volume</c> do Windows aceita para FAT32.</summary>
        private const int SeedPartitionSizeMb = 128;

        internal const string VolumeLabel = "CIDATA";
        private const string SuccessMarker = "SEED_OK:";

        public int CreateSeedPartition(int diskIndex)
        {
            string output = ElevatedPowerShellRunner.Run(
                BuildCreateScript(diskIndex),
                "criação da partição de configuração da instalação automática");

            return ExtractPartitionNumberOrThrow(output);
        }

        public void WriteSeedFiles(int diskIndex, int partitionNumber, string userData, string metaData)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userData);
            ArgumentException.ThrowIfNullOrWhiteSpace(metaData);

            ElevatedPowerShellRunner.Run(
                BuildWriteScript(diskIndex, partitionNumber, userData, metaData),
                "gravação da configuração da instalação automática");
        }

        internal static string BuildCreateScript(int diskIndex) => $@"
$ErrorActionPreference = 'Stop'

$partition = New-Partition -DiskNumber {diskIndex} -Size {SeedPartitionSizeMb}MB -AssignDriveLetter
$letter = $partition.DriveLetter
if (-not $letter) {{ throw ""O Windows não atribuiu letra de unidade à partição de configuração criada no disco {diskIndex}."" }}

Format-Volume -DriveLetter $letter -FileSystem FAT32 -NewFileSystemLabel '{VolumeLabel}' -Force -Confirm:$false | Out-Null

# Tira a letra de unidade: a partição não é para o usuário mexer, e uma unidade nova
# aparecendo no Explorador logo antes de reiniciar para instalar convida a exatamente isso.
Remove-PartitionAccessPath -DiskNumber {diskIndex} -PartitionNumber $partition.PartitionNumber -AccessPath ""$letter`:\""

Write-Output ""{SuccessMarker} $($partition.PartitionNumber)""";

        /// <summary>
        /// O conteúdo dos arquivos viaja em base64 e é gravado com <c>WriteAllBytes</c> em
        /// vez de ir num here-string do PowerShell. Um here-string carregaria as quebras de
        /// linha do próprio arquivo .ps1 (CRLF no Windows) para dentro do YAML, e
        /// <c>Set-Content -Encoding UTF8</c> no Windows PowerShell 5.1 ainda escreveria um
        /// BOM no início — que quebraria a detecção do <c>#cloud-config</c> na primeira
        /// linha. Base64 preserva os bytes exatos.
        /// </summary>
        internal static string BuildWriteScript(
            int diskIndex, int partitionNumber, string userData, string metaData)
        {
            string userDataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(userData));
            string metaDataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(metaData));

            return $@"
$ErrorActionPreference = 'Stop'

Add-PartitionAccessPath -DiskNumber {diskIndex} -PartitionNumber {partitionNumber} -AssignDriveLetter
$letter = (Get-Partition -DiskNumber {diskIndex} -PartitionNumber {partitionNumber}).DriveLetter
if (-not $letter) {{ throw ""Não foi possível montar a partição de configuração ({partitionNumber}) do disco {diskIndex} para gravar o autoinstall."" }}

try {{
    [System.IO.File]::WriteAllBytes(""$letter`:\user-data"", [Convert]::FromBase64String('{userDataBase64}'))
    [System.IO.File]::WriteAllBytes(""$letter`:\meta-data"", [Convert]::FromBase64String('{metaDataBase64}'))
}} finally {{
    Remove-PartitionAccessPath -DiskNumber {diskIndex} -PartitionNumber {partitionNumber} -AccessPath ""$letter`:\""
}}

Write-Output ""{SuccessMarker} {partitionNumber}""";
        }

        internal static int ExtractPartitionNumberOrThrow(string output)
        {
            Match match = Regex.Match(output ?? string.Empty, $@"{SuccessMarker}\s*(\d+)");

            if (!match.Success)
            {
                throw new InvalidOperationException(
                    "A partição de configuração da instalação automática foi criada, mas o Windows " +
                    "não informou o número dela — sem esse número a instalação não consegue " +
                    $"continuar. Saída recebida: {output}");
            }

            return int.Parse(match.Groups[1].Value);
        }
    }
}
