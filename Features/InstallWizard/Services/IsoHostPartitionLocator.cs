using System.Globalization;
using System.IO;
using System.Management;

namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IIsoHostPartitionLocator
    {
        /// <summary>
        /// PARTUUID da partição que hospeda o arquivo indicado, no formato que o Linux usa —
        /// GUID minúsculo sem chaves em GPT, <c>assinatura-NN</c> em MBR.
        /// </summary>
        string GetPartitionUuid(string windowsPath);
    }

    /// <summary>
    /// Descobre, do lado do Windows, o identificador estável da partição onde a ISO está.
    ///
    /// Existe porque o GRUB embutido do app não tem o comando <c>probe</c> (a imagem é gerada
    /// com uma lista fixa de módulos, e não acompanha diretório de módulos para
    /// <c>insmod</c>) — descoberto num boot real, que morreu em "can't find command `probe`" e,
    /// em seguida, num <c>img_dev=</c> vazio. Sem poder perguntar em tempo de boot, quem
    /// precisa nomear a partição é o app.
    ///
    /// Usa o mesmo namespace WMI de <see cref="DiskLayoutProvider"/> e
    /// <see cref="EspLocatorService"/>: <c>MSFT_Partition.Guid</c> é o GUID GPT da partição, o
    /// mesmo valor que o Linux expõe como PARTUUID.
    /// </summary>
    public sealed class IsoHostPartitionLocator : IIsoHostPartitionLocator
    {
        private const string StorageNamespace = @"root\Microsoft\Windows\Storage";

        public string GetPartitionUuid(string windowsPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(windowsPath);

            string root = Path.GetPathRoot(windowsPath)
                ?? throw new InvalidOperationException(
                    $"Não foi possível determinar a unidade de '{windowsPath}'.");

            char driveLetter = char.ToUpperInvariant(root[0]);
            var scope = new ManagementScope(StorageNamespace);

            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery(
                    "SELECT DiskNumber, PartitionNumber, DriveLetter, Guid FROM MSFT_Partition " +
                    $"WHERE DriveLetter = '{driveLetter}'"));

            ManagementBaseObject partition = searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"Não foi possível identificar a partição da unidade {driveLetter}:, onde a " +
                    "ISO está. Sem esse identificador o instalador não teria como encontrá-la " +
                    "depois de reiniciar.");

            // GPT: o GUID da partição É o PARTUUID que o Linux enxerga, só muda a forma.
            string guid = partition["Guid"]?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(guid) && !IsEmptyGuid(guid))
                return Normalize(guid);

            // MBR não tem GUID por partição: o PARTUUID que o util-linux monta é a assinatura
            // do disco mais o número da partição.
            return BuildMbrPartitionUuid(
                scope,
                Convert.ToInt32(partition["DiskNumber"]),
                Convert.ToInt32(partition["PartitionNumber"]),
                driveLetter);
        }

        private static string BuildMbrPartitionUuid(
            ManagementScope scope, int diskNumber, int partitionNumber, char driveLetter)
        {
            using var diskSearcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery($"SELECT Signature FROM MSFT_Disk WHERE Number = {diskNumber}"));

            ManagementBaseObject? disk = diskSearcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
            uint signature = disk?["Signature"] is { } raw ? Convert.ToUInt32(raw) : 0;

            if (signature == 0)
            {
                throw new InvalidOperationException(
                    $"A unidade {driveLetter}:, onde a ISO está, não tem um identificador estável " +
                    "de partição (nem GUID de GPT, nem assinatura de MBR). A instalação foi " +
                    "interrompida: sem ele, o instalador teria que adivinhar em qual disco procurar.");
            }

            return string.Format(
                CultureInfo.InvariantCulture, "{0:x8}-{1:x2}", signature, partitionNumber);
        }

        private static bool IsEmptyGuid(string guid) =>
            Guid.TryParse(guid.Trim().Trim('{', '}'), out Guid parsed) && parsed == Guid.Empty;

        private static string Normalize(string guid) =>
            guid.Trim().Trim('{', '}').ToLowerInvariant();
    }
}
