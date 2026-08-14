using System.Management;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Lê disco e partições pelo namespace <c>root\Microsoft\Windows\Storage</c> (MSFT_Disk /
    /// MSFT_Partition), o mesmo que <see cref="EspLocatorService"/> usa. As classes
    /// <c>Win32_*</c> não servem aqui: <c>Win32_DiskPartition</c> numera a partir de zero e
    /// não expõe o GUID de tipo GPT, e os dois dados precisam estar exatos no storage config
    /// do curtin.
    /// </summary>
    public sealed class DiskLayoutProvider : IDiskLayoutProvider
    {
        private const string StorageNamespace = @"root\Microsoft\Windows\Storage";
        private const string EfiSystemPartitionGptType = "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}";
        private const ushort GptPartitionStyle = 2;

        public DiskLayout GetLayout(int diskIndex)
        {
            var scope = new ManagementScope(StorageNamespace);

            using var diskSearcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery(
                    "SELECT Number, SerialNumber, FriendlyName, Size, PartitionStyle, Signature, " +
                    "Guid, UniqueId, LogicalSectorSize " +
                    $"FROM MSFT_Disk WHERE Number = {diskIndex}"));

            ManagementBaseObject disk = diskSearcher.Get().Cast<ManagementBaseObject>().FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"Disco {diskIndex} não encontrado — o plano de instalação não pode ser montado " +
                    "sem o layout real do disco.");

            long sizeBytes = Convert.ToInt64(disk["Size"] ?? 0L);
            bool isGpt = Convert.ToUInt16(disk["PartitionStyle"] ?? (ushort)0) == GptPartitionStyle;
            IReadOnlyList<long> allSizes = ReadAllDiskSizes(scope);

            // A assinatura só existe (e só identifica) em disco MBR — MSFT_Disk.Signature é
            // 0 num disco GPT, onde quem identifica é o GUID da partição semente.
            string signatureHex = isGpt ? string.Empty : FormatSignature(disk["Signature"]);
            bool hasUniqueSignature = !isGpt && signatureHex.Length > 0 &&
                IsUniqueDiskSignature(signatureHex, scope);
            int logicalSectorSize = Convert.ToInt32(disk["LogicalSectorSize"] ?? 512);
            if (logicalSectorSize is not (512 or 4096))
                logicalSectorSize = 512;

            return new DiskLayout(
                Index: diskIndex,
                SerialNumber: disk["SerialNumber"]?.ToString()?.Trim() ?? string.Empty,
                Model: disk["FriendlyName"]?.ToString()?.Trim() ?? string.Empty,
                SizeBytes: sizeBytes,
                IsGpt: isGpt,
                IsLargestDisk: IsUniqueExtreme(sizeBytes, allSizes, largest: true),
                IsSmallestDisk: IsUniqueExtreme(sizeBytes, allSizes, largest: false),
                Partitions: ReadPartitions(scope, diskIndex),
                DiskSignatureHex: signatureHex,
                HasUniqueDiskSignature: hasUniqueSignature,
                Guid: NormalizeGuid(disk["Guid"]?.ToString()),
                UniqueId: disk["UniqueId"]?.ToString()?.Trim() ?? string.Empty,
                LogicalSectorSizeBytes: logicalSectorSize);
        }

        internal static string NormalizeGuid(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string trimmed = raw.Trim().Trim('{', '}');
            return Guid.TryParse(trimmed, out Guid guid) ? guid.ToString("D") : string.Empty;
        }

        /// <summary>8 dígitos hex minúsculos sem prefixo, mesmo formato que o `blkid` do Linux
        /// reporta em <c>PTUUID</c> para tabela <c>dos</c>. Zero (disco nunca inicializado por
        /// um Windows específico) vira string vazia — não é um identificador utilizável.</summary>
        private static string FormatSignature(object? rawSignature)
        {
            uint signature = Convert.ToUInt32(rawSignature ?? 0u);
            return signature == 0 ? string.Empty : signature.ToString("x8");
        }

        /// <summary>Verdadeiro só se nenhum outro disco da máquina reportar a mesma assinatura.
        /// Discos clonados por imagem (`dd`, restauração de backup) podem colidir — colisão de
        /// assinatura MBR é um problema documentado do próprio Windows, então uma assinatura
        /// repetida não pode ser tratada como identidade.</summary>
        private static bool IsUniqueDiskSignature(string signatureHex, ManagementScope scope)
        {
            using var searcher = new ManagementObjectSearcher(
                scope, new ObjectQuery("SELECT Signature, PartitionStyle FROM MSFT_Disk"));

            int matches = 0;
            foreach (ManagementBaseObject otherDisk in searcher.Get())
            {
                if (Convert.ToUInt16(otherDisk["PartitionStyle"] ?? (ushort)0) == GptPartitionStyle)
                    continue;

                if (FormatSignature(otherDisk["Signature"]) == signatureHex)
                    matches++;
            }

            return matches == 1;
        }

        private static List<long> ReadAllDiskSizes(ManagementScope scope)
        {
            using var searcher = new ManagementObjectSearcher(
                scope, new ObjectQuery("SELECT Size FROM MSFT_Disk"));

            var sizes = new List<long>();
            foreach (ManagementBaseObject disk in searcher.Get())
                sizes.Add(Convert.ToInt64(disk["Size"] ?? 0L));

            return sizes;
        }

        /// <summary>
        /// Se <paramref name="sizeBytes"/> é o maior (ou menor) tamanho da lista E nenhum
        /// outro disco empata com ele. O empate importa: <c>size: largest</c> com dois discos
        /// do mesmo tamanho no topo escolhe um dos dois sem critério — e o critério aqui não
        /// pode ser "qualquer um".
        /// </summary>
        private static bool IsUniqueExtreme(long sizeBytes, IReadOnlyList<long> allSizes, bool largest)
        {
            long extreme = largest ? allSizes.Max() : allSizes.Min();

            return sizeBytes == extreme && allSizes.Count(size => size == extreme) == 1;
        }

        private static List<PartitionLayout> ReadPartitions(ManagementScope scope, int diskIndex)
        {
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery(
                    "SELECT PartitionNumber, Offset, Size, GptType, MbrType, IsActive, IsHidden, Guid " +
                    $"FROM MSFT_Partition WHERE DiskNumber = {diskIndex}"));

            var partitions = new List<PartitionLayout>();

            foreach (ManagementBaseObject partition in searcher.Get())
            {
                string gptType = partition["GptType"]?.ToString() ?? string.Empty;

                partitions.Add(new PartitionLayout(
                    Number: Convert.ToInt32(partition["PartitionNumber"]),
                    OffsetBytes: Convert.ToInt64(partition["Offset"] ?? 0L),
                    SizeBytes: Convert.ToInt64(partition["Size"] ?? 0L),
                    GptType: gptType,
                    IsEfiSystemPartition: string.Equals(
                        gptType, EfiSystemPartitionGptType, StringComparison.OrdinalIgnoreCase),
                    // MbrType e IsActive só têm valor numa tabela MBR, mas são lidos sempre:
                    // é o mesmo WMI, e deixar de ler significaria descobrir tarde demais que
                    // o disco não era GPT.
                    MbrType: Convert.ToInt32(partition["MbrType"] ?? 0),
                    IsActive: partition["IsActive"] is bool active && active,
                    // Idem para o GUID: só existe em GPT, mas ler sempre evita uma segunda
                    // query quando a partição semente precisa ser identificada depois.
                    Guid: partition["Guid"]?.ToString()?.Trim() ?? string.Empty));
            }

            return partitions.OrderBy(p => p.Number).ToList();
        }
    }
}
