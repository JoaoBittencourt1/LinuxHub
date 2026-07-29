namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Usa <c>Resize-Partition</c> em vez de <c>diskpart</c>. O script diskpart anterior fazia
    /// <c>select disk</c> + <c>select partition</c> + <c>shrink</c>, mas o <c>shrink</c> do
    /// diskpart age sobre o VOLUME em foco, e <c>select partition</c> não coloca volume nenhum
    /// em foco — o comando morria com "Não há volume em foco. Selecione um volume e tente
    /// novamente." (erro real em teste). Pior: uma partição sem volume (MSR, por exemplo) é
    /// selecionável por número, então o modo antigo podia mirar silenciosamente na partição
    /// errada. <c>Resize-Partition</c> endereça por disco+número de partição direto, sem
    /// nenhuma noção de "foco", e usa a mesma numeração que <c>Get-Partition</c>.
    ///
    /// Roda pelo <see cref="ElevatedPowerShellRunner"/> em vez de repetir o boilerplate de
    /// elevação — que também garante que a operação fique registrada no log persistente.
    /// </summary>
    public sealed class DiskPartitioningService : IDiskPartitioningService
    {
        public void ShrinkPartition(int diskIndex, int partitionIndex, long bytesToFree)
        {
            ElevatedPowerShellRunner.Run(
                BuildScript(diskIndex, partitionIndex, bytesToFree),
                $"redimensionamento da partição {partitionIndex} do disco {diskIndex}");
        }

        public void EnsureUnallocatedSpace(int diskIndex, long requiredBytes)
        {
            ElevatedPowerShellRunner.Run(
                BuildEnsureSpaceScript(diskIndex, requiredBytes),
                $"abertura de espaço não alocado no disco {diskIndex}");
        }

        /// <summary>
        /// Confere <c>Get-PartitionSupportedSize</c> ANTES de encolher. Sem essa checagem o
        /// erro que chega ao usuário é o do próprio cmdlet, que não diz quanto de fato dava
        /// pra liberar — e o motivo mais comum de um shrink falhar no Windows é arquivo
        /// imóvel (paginação, hibernação, restauração do sistema), não falta de espaço livre.
        /// É por isso que este número não bate com o teto do slider, que só enxerga espaço
        /// livre (ver <c>TargetSelectionViewModel</c>): 38 GB livres podem render 21 GB de
        /// shrink. Este é o número autoritativo, mas exige elevação — por isso só aqui.
        /// <paramref name="bytesToFree"/> é quanto LIBERAR, não o tamanho final.
        /// </summary>
        internal static string BuildScript(int diskIndex, int partitionIndex, long bytesToFree) => $@"
$ErrorActionPreference = 'Stop'
$partition = Get-Partition -DiskNumber {diskIndex} -PartitionNumber {partitionIndex}

# Terceira (e última) barreira contra encolher um filesystem que o Windows não sabe
# encolher — as outras duas são o filtro de NTFS em PartitionInventoryService e o
# CalculateShrinkableGb em TargetSelectionViewModel. Precisa existir aqui também porque
# este é o único ponto que roda elevado e imediatamente antes da escrita: a checagem de
# Get-PartitionSupportedSize logo abaixo NÃO protege nada numa ext4 (SizeMin volta como
# 1 MiB), então sem isto o Resize-Partition trunca o filesystem silenciosamente.
$volume = $null
try {{ $volume = $partition | Get-Volume -ErrorAction Stop }} catch {{ $volume = $null }}
if ($null -eq $volume -or $volume.FileSystem -ne 'NTFS') {{
    $fsAtual = if ($null -eq $volume -or [string]::IsNullOrWhiteSpace($volume.FileSystem)) {{ 'nenhum que o Windows reconheça' }} else {{ $volume.FileSystem }}
    throw ""A partição {partitionIndex} do disco {diskIndex} não é NTFS (filesystem: $fsAtual) e não pode ser encolhida com segurança. Encolher uma partição não-NTFS (ext4, LVM, swap) corta o filesystem sem mover o conteúdo, destruindo o sistema instalado nela. Escolha uma partição do Windows.""
}}

$supported = Get-PartitionSupportedSize -DiskNumber {diskIndex} -PartitionNumber {partitionIndex}
$newSize = $partition.Size - {bytesToFree}
if ($newSize -lt $supported.SizeMin) {{
    $maxGb = [math]::Floor(($partition.Size - $supported.SizeMin) / 1GB)
    $pedidoGb = [math]::Ceiling({bytesToFree} / 1GB)
    throw ""O Windows só consegue liberar $maxGb GB na partição {partitionIndex} do disco {diskIndex}, e não os $pedidoGb GB necessários. Arquivos imóveis (paginação, hibernação, restauração do sistema) limitam o encolhimento mesmo havendo espaço livre. Volte e escolha no máximo $maxGb GB, ou desative a hibernação e a restauração do sistema para liberar mais.""
}}
Resize-Partition -DiskNumber {diskIndex} -PartitionNumber {partitionIndex} -Size $newSize
Write-Output ""SHRINK_OK: partição {partitionIndex} do disco {diskIndex} agora tem $([math]::Round($newSize / 1GB, 1)) GB""";

        /// <summary>
        /// Abre espaço sem alvo informado — usado no modo substituir, onde o usuário escolheu
        /// um disco e não uma partição. A maior NTFS do disco é a candidata: num layout de
        /// Windows típico é o C:, a única com folga real; Recovery e ESP são pequenas demais e
        /// a MSR não tem filesystem nenhum.
        ///
        /// Sai sem fazer nada quando já há espaço: no dual-boot o encolhimento do slider já
        /// abriu o vão, e encolher de novo roubaria espaço do usuário silenciosamente.
        /// </summary>
        internal static string BuildEnsureSpaceScript(int diskIndex, long requiredBytes) => $@"
$ErrorActionPreference = 'Stop'

if ((Get-Disk -Number {diskIndex}).LargestFreeExtent -ge {requiredBytes}) {{
    Write-Output ""SPACE_OK: disco {diskIndex} já tem o espaço não alocado necessário""
    return
}}

$candidata = Get-Partition -DiskNumber {diskIndex} | ForEach-Object {{
    $vol = $null
    try {{ $vol = $_ | Get-Volume -ErrorAction Stop }} catch {{ $vol = $null }}
    if ($null -ne $vol -and $vol.FileSystem -eq 'NTFS') {{ $_ }}
}} | Sort-Object Size -Descending | Select-Object -First 1

if ($null -eq $candidata) {{
    $pedidoGb = [math]::Round({requiredBytes} / 1GB, 1)
    throw ""O disco {diskIndex} não tem os $pedidoGb GB não alocados necessários para preparar a instalação, e nenhuma partição NTFS que pudesse ser encolhida para abrir esse espaço. Libere espaço no disco e tente novamente.""
}}

$suportado = Get-PartitionSupportedSize -DiskNumber {diskIndex} -PartitionNumber $candidata.PartitionNumber
$novoTamanho = $candidata.Size - {requiredBytes}
if ($novoTamanho -lt $suportado.SizeMin) {{
    $maxGb = [math]::Floor(($candidata.Size - $suportado.SizeMin) / 1GB)
    $pedidoGb = [math]::Round({requiredBytes} / 1GB, 1)
    throw ""A partição $($candidata.PartitionNumber) do disco {diskIndex} só pode ser encolhida em $maxGb GB, e a preparação da instalação precisa de $pedidoGb GB. Arquivos imóveis (paginação, hibernação, restauração do sistema) limitam o encolhimento mesmo havendo espaço livre.""
}}

Resize-Partition -DiskNumber {diskIndex} -PartitionNumber $candidata.PartitionNumber -Size $novoTamanho
Write-Output ""SPACE_OK: liberados $([math]::Round({requiredBytes} / 1GB, 1)) GB no disco {diskIndex}""";
    }
}
