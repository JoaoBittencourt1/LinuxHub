namespace LinuxHub.Features.InstallWizard.Models
{
    /// <summary>
    /// Uma partição existente no disco alvo, com os números exatos que o storage config do
    /// curtin precisa para reconhecê-la como preservada. Diferente de
    /// <see cref="LinuxHub.Common.Models.PartitionInfo"/>, que existe para a UI escolher um
    /// alvo de shrink — aqui nada é estimativa: um offset errado faz o curtin recriar a
    /// partição do Windows em vez de preservá-la.
    /// </summary>
    /// <param name="GptType">GUID de tipo GPT como o WMI reporta (entre chaves, minúsculo).
    /// Precisa ser carregado até o autoinstall: <c>preserve: true</c> NÃO preserva o tipo.
    /// Ver <c>AutoinstallStorageBuilder.PartitionTypeOf</c>.</param>
    /// <param name="MbrType">Byte de tipo da tabela MBR (<c>MSFT_Partition.MbrType</c>:
    /// 0x07 = NTFS/IFS, 0x0C = FAT32 LBA…). Vale zero num disco GPT, onde quem manda é
    /// <paramref name="GptType"/>.</param>
    /// <param name="IsActive">Flag "ativa" do MBR. Só existe em tabela MBR e é o que a BIOS
    /// procura para bootar — perdê-la deixa o Windows sem boot num disco legado.</param>
    public sealed record PartitionLayout(
        int Number,
        long OffsetBytes,
        long SizeBytes,
        string GptType,
        bool IsEfiSystemPartition,
        int MbrType = 0,
        bool IsActive = false);

    /// <summary>
    /// Foto do disco alvo no momento em que o plano é montado. Serve de entrada para o
    /// <c>AutoinstallStorageBuilder</c>.
    /// </summary>
    /// <param name="SerialNumber">Como o WINDOWS reporta. Num NVMe isso é o EUI-64 do
    /// namespace formatado pela Microsoft (<c>0000_0000_..._7C70.</c>), e não o número de
    /// série do controlador que o Linux expõe em <c>serial</c> — os dois nunca batem. Serve
    /// para diagnóstico, nunca para casar o disco do lado Linux (ver
    /// <c>AutoinstallStorageBuilder.BuildDiskMatch</c>).</param>
    /// <param name="IsLargestDisk">Se este é o maior disco da máquina, e sozinho nesse posto
    /// (empate de tamanho não conta). Idem <paramref name="IsSmallestDisk"/> para o menor.
    /// Numa máquina de um disco só os dois são verdadeiros.
    ///
    /// Os dois existem porque são a única forma de identificar o disco no autoinstall sem
    /// depender de um nome que atravesse Windows e Linux — ver
    /// <c>AutoinstallStorageBuilder.BuildDiskMatch</c>.</param>
    public sealed record DiskLayout(
        int Index,
        string SerialNumber,
        string Model,
        long SizeBytes,
        bool IsGpt,
        bool IsLargestDisk,
        bool IsSmallestDisk,
        IReadOnlyList<PartitionLayout> Partitions)
    {
        /// <summary>
        /// Maior trecho contíguo não alocado do disco — onde a partição Linux nova vai
        /// nascer, tipicamente o espaço que o shrink acabou de liberar. Devolve
        /// <c>(0, 0)</c> quando não há espaço livre.
        ///
        /// O último setor do disco não é utilizável num disco GPT (a cópia de segurança da
        /// tabela de partição mora lá), por isso a reserva no fim.
        /// </summary>
        public (long OffsetBytes, long SizeBytes) FindLargestFreeGap()
        {
            const long GptBackupReserveBytes = 1024 * 1024;

            long bestOffset = 0;
            long bestSize = 0;
            long cursor = 0;

            foreach (PartitionLayout partition in Partitions.OrderBy(p => p.OffsetBytes))
            {
                long gap = partition.OffsetBytes - cursor;
                if (gap > bestSize)
                {
                    bestSize = gap;
                    bestOffset = cursor;
                }

                cursor = Math.Max(cursor, partition.OffsetBytes + partition.SizeBytes);
            }

            long trailingGap = SizeBytes - GptBackupReserveBytes - cursor;
            if (trailingGap > bestSize)
            {
                bestSize = trailingGap;
                bestOffset = cursor;
            }

            return bestSize > 0 ? (bestOffset, bestSize) : (0, 0);
        }

        public PartitionLayout? EfiSystemPartition =>
            Partitions.FirstOrDefault(p => p.IsEfiSystemPartition);

        /// <summary>Próximo número de partição livre — o que a partição Linux nova vai
        /// receber. Não é <c>Count + 1</c>: numeração com buracos (uma partição removida no
        /// meio) é perfeitamente válida em GPT.</summary>
        public int NextFreePartitionNumber =>
            Partitions.Count == 0 ? 1 : Partitions.Max(p => p.Number) + 1;
    }
}
