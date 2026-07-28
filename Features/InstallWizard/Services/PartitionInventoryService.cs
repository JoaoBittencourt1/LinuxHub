using System.Management;
using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Lê as partições candidatas a encolhimento pelo namespace <c>root\Microsoft\Windows\Storage</c>
    /// (<c>MSFT_Partition</c>), o mesmo que <see cref="DiskLayoutProvider"/> e
    /// <see cref="EspLocatorService"/> usam.
    ///
    /// NÃO usa <c>Win32_DiskPartition</c>. Aquela classe numera a partir de zero e a versão
    /// anterior deste service convertia para a numeração do <c>Resize-Partition</c> somando 1 —
    /// partindo do princípio de que as duas listas têm as mesmas entradas na mesma ordem. Não
    /// têm: o <c>Win32_DiskPartition</c> OMITE a partição reservada da Microsoft (MSR). Num
    /// disco Windows comum (1 = ESP, 2 = MSR, 3 = C:) ele lista só duas entradas, e o
    /// <c>Index + 1</c> do C: dá 2 — que é a MSR. O app mandou encolher a MSR num teste real; só
    /// não destruiu nada porque a checagem de NTFS em <see cref="DiskPartitioningService"/>
    /// barrou na hora.
    ///
    /// O deslocamento nem sempre aparece, o que torna o bug antigo especialmente traiçoeiro:
    /// enquanto a MSR estava com o GUID de tipo errado (ver
    /// <c>AutoinstallStorageBuilder.PartitionTypeOf</c>), o Windows a listava como partição
    /// comum e a soma batia por acidente. <c>MSFT_Partition.PartitionNumber</c> é o número
    /// autoritativo, o mesmo que o <c>Resize-Partition</c> endereça — então não há conversão
    /// nenhuma a fazer aqui.
    /// </summary>
    public sealed class PartitionInventoryService : IPartitionInventoryService
    {
        private const string StorageNamespace = @"root\Microsoft\Windows\Storage";

        private const long MinimumEligibleSizeBytes = 20L * 1024 * 1024 * 1024;

        /// <summary>
        /// Único filesystem que o Windows sabe encolher preservando o conteúdo. Ver
        /// <see cref="PartitionInfo.FileSystem"/> para por que isto é um filtro duro e
        /// não uma preferência.
        /// </summary>
        private const string ShrinkableFileSystem = "NTFS";

        public IReadOnlyList<PartitionInfo> GetEligiblePartitions()
        {
            var scope = new ManagementScope(StorageNamespace);
            IReadOnlyDictionary<char, VolumeInfo> volumes = ReadVolumesByDriveLetter(scope);

            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT DiskNumber, PartitionNumber, Size, DriveLetter FROM MSFT_Partition"));

            var partitions = new List<PartitionInfo>();

            foreach (ManagementBaseObject partition in searcher.Get())
            {
                long size = Convert.ToInt64(partition["Size"] ?? 0L);

                // Ignora partições muito pequenas (EFI, MSR, Recovery)
                if (size < MinimumEligibleSizeBytes)
                    continue;

                // Sem letra de unidade não há volume montado, e sem volume o Windows não
                // reporta filesystem — que para o filtro abaixo significa "não é NTFS".
                if (!volumes.TryGetValue(ReadDriveLetter(partition), out VolumeInfo volume))
                    continue;

                // Só NTFS é alvo válido de shrink. Uma partição Linux entra aqui com
                // fileSystem == null (o Windows a enxerga como RAW) e precisa sair da
                // lista ANTES de chegar na UI — enquanto ela era listada, o wizard
                // oferecia a raiz de um Linux instalado como alvo de encolhimento.
                if (!string.Equals(volume.FileSystem, ShrinkableFileSystem, StringComparison.OrdinalIgnoreCase))
                    continue;

                partitions.Add(new PartitionInfo
                {
                    DiskIndex = Convert.ToInt32(partition["DiskNumber"] ?? 0),
                    PartitionIndex = Convert.ToInt32(partition["PartitionNumber"] ?? 0),
                    SizeBytes = size,
                    FreeSpaceBytes = volume.FreeSpaceBytes,
                    FileSystem = volume.FileSystem
                });
            }

            return partitions
                .OrderBy(p => p.DiskIndex)
                .ThenBy(p => p.PartitionIndex)
                .ToList();
        }

        private readonly record struct VolumeInfo(long FreeSpaceBytes, string FileSystem);

        /// <summary>
        /// Espaço livre e filesystem de cada volume montado, indexados pela letra de unidade —
        /// que é como <c>MSFT_Partition</c> e <c>MSFT_Volume</c> se ligam sem depender de uma
        /// consulta de associação. Uma leitura só para todos os volumes, em vez de uma por
        /// partição.
        ///
        /// É o único caminho que funciona SEM elevação: <c>Get-PartitionSupportedSize</c> (o
        /// número autoritativo do shrink) exige administrador, e pedir UAC só pra montar a
        /// lista de partições do wizard seria pior do que estimar aqui.
        /// </summary>
        private static Dictionary<char, VolumeInfo> ReadVolumesByDriveLetter(ManagementScope scope)
        {
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT DriveLetter, FileSystem, SizeRemaining FROM MSFT_Volume"));

            var volumes = new Dictionary<char, VolumeInfo>();

            foreach (ManagementBaseObject volume in searcher.Get())
            {
                char driveLetter = ReadDriveLetter(volume);
                if (driveLetter == '\0')
                    continue;

                volumes[driveLetter] = new VolumeInfo(
                    Convert.ToInt64(volume["SizeRemaining"] ?? 0L),
                    volume["FileSystem"]?.ToString() ?? string.Empty);
            }

            return volumes;
        }

        /// <summary>
        /// A letra de unidade vem como <c>char16</c> do WMI e chega ora como <see cref="char"/>,
        /// ora como <see cref="ushort"/>, dependendo do marshalling. Uma partição sem letra
        /// devolve o caractere nulo, nunca uma letra de outra partição.
        /// </summary>
        private static char ReadDriveLetter(ManagementBaseObject wmiObject) =>
            wmiObject["DriveLetter"] switch
            {
                char letter => char.ToUpperInvariant(letter),
                ushort code when code != 0 => char.ToUpperInvariant((char)code),
                _ => '\0'
            };
    }
}
