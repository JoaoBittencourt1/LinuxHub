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
        /// <paramref name="sizeInGb"/> é quanto LIBERAR, não o tamanho final.
        /// </summary>
        internal static string BuildScript(int diskIndex, int partitionIndex, int sizeInGb) => $@"
$ErrorActionPreference = 'Stop'
$partition = Get-Partition -DiskNumber {diskIndex} -PartitionNumber {partitionIndex}
$supported = Get-PartitionSupportedSize -DiskNumber {diskIndex} -PartitionNumber {partitionIndex}
$newSize = $partition.Size - {sizeInGb}GB
if ($newSize -lt $supported.SizeMin) {{
    $maxGb = [math]::Floor(($partition.Size - $supported.SizeMin) / 1GB)
    throw ""Não dá para liberar {sizeInGb} GB na partição {partitionIndex} do disco {diskIndex}: o máximo liberável aqui é $maxGb GB. Arquivos imóveis do Windows (paginação, hibernação, restauração do sistema) limitam o encolhimento.""
}}
Resize-Partition -DiskNumber {diskIndex} -PartitionNumber {partitionIndex} -Size $newSize
Write-Output ""SHRINK_OK: partição {partitionIndex} do disco {diskIndex} agora tem $([math]::Round($newSize / 1GB, 1)) GB""";
    }
}
