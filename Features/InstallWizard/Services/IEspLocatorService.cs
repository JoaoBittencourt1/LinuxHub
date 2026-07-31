namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>Onde uma EFI System Partition está: disco E número da partição. O disco faz
    /// parte da resposta porque a ESP não fica necessariamente no disco que a instalação tem
    /// como alvo — numa máquina de vários discos ela mora no disco de boot.</summary>
    public sealed record EfiSystemPartitionLocation(int DiskIndex, int PartitionIndex);

    public interface IEspLocatorService
    {
        /// <summary>
        /// Localiza o número da EFI System Partition no disco indicado, pelo GUID de
        /// tipo GPT (c12a7328-f81f-11d2-ba4b-00a0c93ec93b), nunca por índice fixo.
        /// Retorna null se o disco não tiver ESP (BIOS legado, ou disco sem GPT).
        /// </summary>
        int? FindEfiSystemPartitionIndex(int diskIndex);

        /// <summary>
        /// A ESP de onde esta máquina bootou, em qualquer disco — identificada pelo flag
        /// <c>MSFT_Partition.IsSystem</c>, que é o Windows dizendo qual partição a firmware
        /// usou, e não uma dedução a partir do disco alvo ou do disco 0. Retorna null quando
        /// não existe nenhuma (boot em BIOS legado).
        /// </summary>
        EfiSystemPartitionLocation? FindSystemEfiSystemPartition();
    }
}
