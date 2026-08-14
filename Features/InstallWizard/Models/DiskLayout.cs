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
    /// <param name="Guid">GUID GPT da partição (entre chaves, como o WMI reporta). Vazio numa
    /// tabela MBR, que não tem esse conceito. É o dado que identifica o disco alvo do lado
    /// Linux sem depender de ranking de tamanho — ver
    /// <c>AutoinstallStorageBuilder.BuildDiskMatch</c>.</param>
    public sealed record PartitionLayout(
        int Number,
        long OffsetBytes,
        long SizeBytes,
        string GptType,
        bool IsEfiSystemPartition,
        int MbrType = 0,
        bool IsActive = false,
        string Guid = "");

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
    /// Servem hoje só de último recurso: o disco alvo agora é identificado preferencialmente
    /// por dado de tabela de partição (GUID GPT da partição semente, ou
    /// <paramref name="DiskSignatureHex"/> em MBR) — ver
    /// <c>AutoinstallStorageBuilder.BuildDiskMatch</c>.</param>
    /// <param name="DiskSignatureHex">Assinatura de disco MBR (offset 0x1B8, 4 bytes), em hex
    /// minúsculo sem prefixo. Vazia num disco GPT, que não tem esse conceito — lá quem
    /// identifica é o GUID da partição semente. É dado de tabela de partição, escrito pelo
    /// Windows e lido pelo Linux (`blkid`/`PTUUID`) sem tradução de driver — mesma categoria
    /// de confiabilidade do PARTUUID GPT.</param>
    /// <param name="HasUniqueDiskSignature">Falso quando a assinatura é zerada (disco nunca
    /// inicializado por um Windows específico) ou quando mais de um disco da máquina reporta a
    /// mesma assinatura (discos clonados por imagem — colisão documentada do próprio Windows).
    /// Nesses casos a assinatura não pode ser usada para identidade, e o match cai no critério
    /// de tamanho.</param>
    public sealed record DiskLayout(
        int Index,
        string SerialNumber,
        string Model,
        long SizeBytes,
        bool IsGpt,
        bool IsLargestDisk,
        bool IsSmallestDisk,
        IReadOnlyList<PartitionLayout> Partitions,
        string DiskSignatureHex = "",
        bool HasUniqueDiskSignature = false,
        string Guid = "",
        string UniqueId = "",
        int LogicalSectorSizeBytes = 512)
    {
        /// <summary>
        /// Maior trecho contíguo utilizável do disco — onde a partição Linux nova vai nascer.
        /// Devolve <c>(0, 0)</c> quando não há espaço.
        ///
        /// <paramref name="preservedPartitionNumbers"/> muda o que conta como ocupado. Sem ele
        /// (dual-boot), toda partição existente ocupa espaço e o resultado é o vão que o shrink
        /// acabou de liberar. Com ele (substituir), só as partições preservadas ocupam — as
        /// demais serão omitidas do storage config e o curtin trata o espaço delas como
        /// disponível, então ele precisa entrar nesta conta. Sem isso, o modo substituir só
        /// enxergaria o vão residual do shrink e criaria uma raiz minúscula em vez de ocupar o
        /// disco.
        ///
        /// O último setor do disco não é utilizável num disco GPT (a cópia de segurança da
        /// tabela de partição mora lá), por isso a reserva no fim.
        /// </summary>
        public (long OffsetBytes, long SizeBytes) FindLargestFreeGap(
            IReadOnlyCollection<int>? preservedPartitionNumbers = null)
        {
            const long GptBackupReserveBytes = 1024 * 1024;

            IEnumerable<PartitionLayout> occupying = preservedPartitionNumbers is null
                ? Partitions
                : Partitions.Where(p => preservedPartitionNumbers.Contains(p.Number));

            long bestOffset = 0;
            long bestSize = 0;
            long cursor = 0;

            foreach (PartitionLayout partition in occupying.OrderBy(p => p.OffsetBytes))
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

        /// <summary>Windows Recovery Environment partition (GPT type
        /// <c>{de94bba4-06d1-4d40-a16a-bfd50179d6ac}</c>), when present.</summary>
        public PartitionLayout? WindowsRecoveryPartition =>
            Partitions.FirstOrDefault(p => string.Equals(
                p.GptType,
                "{de94bba4-06d1-4d40-a16a-bfd50179d6ac}",
                StringComparison.OrdinalIgnoreCase));

        /// <summary>Active MBR partition — what BIOS firmware boots.</summary>
        public PartitionLayout? ActiveMbrPartition =>
            Partitions.FirstOrDefault(p => p.IsActive);

        /// <summary>Próximo número de partição livre — o que a partição Linux nova vai
        /// receber. Não é <c>Count + 1</c>: numeração com buracos (uma partição removida no
        /// meio) é perfeitamente válida em GPT.</summary>
        public int NextFreePartitionNumber =>
            Partitions.Count == 0 ? 1 : Partitions.Max(p => p.Number) + 1;
    }
}
