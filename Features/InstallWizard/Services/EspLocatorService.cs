using System.Management;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed class EspLocatorService : IEspLocatorService
    {
        private const string StorageNamespace = @"root\Microsoft\Windows\Storage";
        private const string EfiSystemPartitionGptType = "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}";

        public int? FindEfiSystemPartitionIndex(int diskIndex)
        {
            var scope = new ManagementScope(StorageNamespace);
            var query = new ObjectQuery(
                $"SELECT PartitionNumber, GptType FROM MSFT_Partition WHERE DiskNumber = {diskIndex}");

            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementBaseObject partition in searcher.Get())
            {
                if (IsEsp(partition))
                    return Convert.ToInt32(partition["PartitionNumber"]);
            }

            return null;
        }

        public EfiSystemPartitionLocation? FindSystemEfiSystemPartition()
        {
            var scope = new ManagementScope(StorageNamespace);
            var query = new ObjectQuery(
                "SELECT DiskNumber, PartitionNumber, GptType, IsSystem FROM MSFT_Partition");

            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementBaseObject partition in searcher.Get())
            {
                // O flag IsSystem é o único dado que distingue A ESP de boot de uma ESP
                // qualquer: uma máquina com mais de um disco pode ter várias, e só uma delas
                // é a que a firmware leu. Casar só pelo GptType devolveria a primeira que
                // aparecesse na enumeração, que é um chute com cara de leitura.
                if (IsEsp(partition) && partition["IsSystem"] is bool isSystem && isSystem)
                {
                    return new EfiSystemPartitionLocation(
                        Convert.ToInt32(partition["DiskNumber"]),
                        Convert.ToInt32(partition["PartitionNumber"]));
                }
            }

            return null;
        }

        private static bool IsEsp(ManagementBaseObject partition) =>
            string.Equals(
                partition["GptType"]?.ToString(),
                EfiSystemPartitionGptType,
                StringComparison.OrdinalIgnoreCase);
    }
}
