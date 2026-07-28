namespace LinuxHub.Common.Models
{
    public class PartitionInfo
    {
        private const double BytesPerGb = 1024d * 1024 * 1024;

        public int DiskIndex { get; set; }

        /// <summary>
        /// Número da partição como <c>MSFT_Partition.PartitionNumber</c> reporta — o mesmo que
        /// <c>Resize-Partition -PartitionNumber</c> endereça. Nunca derivado de um índice de
        /// outra fonte por aritmética: ver <c>PartitionInventoryService</c> para o bug que isso
        /// causou.
        /// </summary>
        public int PartitionIndex { get; set; }

        public long SizeBytes { get; set; }

        /// <summary>
        /// Espaço livre do volume montado nesta partição, ou <c>null</c> quando o Windows
        /// não reporta nenhum (partição sem letra, sem volume associado). É o TETO absoluto
        /// de quanto um shrink pode liberar — o Windows nunca encolhe além do espaço livre.
        /// Ver <c>TargetSelectionViewModel</c>, que dimensiona o slider a partir daqui.
        /// </summary>
        public long? FreeSpaceBytes { get; set; }

        /// <summary>
        /// Sistema de arquivos que o Windows reconhece no volume desta partição
        /// (<c>NTFS</c>, <c>FAT32</c>, …), ou <c>null</c> quando ele não reconhece nenhum —
        /// caso de qualquer partição Linux (ext4, LVM, swap), que para o Windows é RAW.
        ///
        /// Encolher uma partição cujo filesystem o Windows não entende TRUNCA esse
        /// filesystem: <c>Resize-Partition</c> só reescreve a entrada da tabela de
        /// partição, sem consultar nem mover nada do conteúdo. Foi assim que uma
        /// instalação Linux real foi destruída em teste (Resize-Partition aceitou
        /// encolher 100 GiB de uma ext4). Por isso este campo é filtro de elegibilidade,
        /// não informação decorativa — ver <c>PartitionInventoryService</c>.
        /// </summary>
        public string? FileSystem { get; set; }

        public override string ToString()
        {
            string sizeGb = (SizeBytes / BytesPerGb).ToString("0");
            string label = $"Disco {DiskIndex} | Partição {PartitionIndex} | {sizeGb} GB";

            // O espaço livre é o que de fato limita o dual-boot, então precisa estar
            // visível na hora de escolher a partição — não só depois, num erro de shrink.
            return FreeSpaceBytes is { } free
                ? $"{label} ({(free / BytesPerGb).ToString("0")} GB livres)"
                : label;
        }
    }
}
