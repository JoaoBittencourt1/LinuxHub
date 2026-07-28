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
        public void ShrinkPartition(int diskIndex, int partitionIndex, int sizeInGb)
        {
            ElevatedPowerShellRunner.Run(
                BuildScript(diskIndex, partitionIndex, sizeInGb),
                $"redimensionamento da partição {partitionIndex} do disco {diskIndex}");
        }

        /// <summary>
        /// Confere <c>Get-PartitionSupportedSize</c> ANTES de encolher. Sem essa checagem o
        /// erro que chega ao usuário é o do próprio cmdlet, que não diz quanto de fato dava
        /// pra liberar — e o motivo mais comum de um shrink falhar no Windows é arquivo
        /// imóvel (paginação, hibernação, restauração do sistema), não falta de espaço livre.
        /// É por isso que este número não bate com o teto do slider, que só enxerga espaço
        /// livre (ver <c>TargetSelectionViewModel</c>): 38 GB livres podem render 21 GB de
        /// shrink. Este é o número autoritativo, mas exige elevação — por isso só aqui.
        /// <paramref name="sizeInGb"/> é quanto LIBERAR, não o tamanho final.
        /// </summary>
        internal static string BuildScript(int diskIndex, int partitionIndex, int sizeInGb) => $@"
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
$newSize = $partition.Size - {sizeInGb}GB
if ($newSize -lt $supported.SizeMin) {{
    $maxGb = [math]::Floor(($partition.Size - $supported.SizeMin) / 1GB)
    throw ""O Windows só consegue liberar $maxGb GB na partição {partitionIndex} do disco {diskIndex}, e não os {sizeInGb} GB pedidos. Arquivos imóveis (paginação, hibernação, restauração do sistema) limitam o encolhimento mesmo havendo espaço livre. Volte e escolha no máximo $maxGb GB, ou desative a hibernação e a restauração do sistema para liberar mais.""
}}
Resize-Partition -DiskNumber {diskIndex} -PartitionNumber {partitionIndex} -Size $newSize
Write-Output ""SHRINK_OK: partição {partitionIndex} do disco {diskIndex} agora tem $([math]::Round($newSize / 1GB, 1)) GB""";
    }
}
