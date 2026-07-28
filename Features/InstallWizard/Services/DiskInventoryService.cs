using System.IO;
using System.Management;
using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed class DiskInventoryService : IDiskInventoryService
    {
        public IReadOnlyList<DiskInfo> GetDisks()
        {
            int systemDiskIndex = FindSystemDiskIndex();

            var disks = new List<DiskInfo>();

            using var searcher = new ManagementObjectSearcher(
                "SELECT Index, Model, Size FROM Win32_DiskDrive");

            foreach (ManagementBaseObject disk in searcher.Get())
            {
                if (disk["Size"] == null)
                    continue;

                int index = Convert.ToInt32(disk["Index"]);

                disks.Add(new DiskInfo
                {
                    Index = index,
                    Model = disk["Model"]?.ToString() ?? "Desconhecido",
                    SizeBytes = Convert.ToInt64(disk["Size"]),
                    IsSystemDisk = index == systemDiskIndex
                });
            }

            return disks;
        }

        /// <summary>
        /// Encontra o disco físico que hospeda o volume de boot do Windows em execução,
        /// via associação WMI (Win32_LogicalDisk → Win32_DiskPartition → Win32_DiskDrive)
        /// — nunca um índice assumido, já que a ordem dos discos varia por máquina.
        /// </summary>
        private static int FindSystemDiskIndex()
        {
            // Sem fallback para "C:": assumir a letra do Windows é assumir o disco do Windows, e
            // é justamente isso que alimenta IsSystemDisk — a flag que dispara a confirmação de
            // "você está prestes a apagar o disco onde o Windows está". Chutar aqui marcaria o
            // disco errado como sistema numa máquina cujo Windows não mora em C:.
            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\')
                ?? throw new InvalidOperationException(
                    "Não foi possível determinar a letra de unidade onde o Windows está instalado " +
                    $"(diretório de sistema: '{Environment.SystemDirectory}').");

            using var partitionSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemDrive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

            foreach (ManagementBaseObject partition in partitionSearcher.Get())
            {
                string partitionDeviceId = partition["DeviceID"]?.ToString() ?? string.Empty;

                using var diskSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                foreach (ManagementBaseObject diskDrive in diskSearcher.Get())
                    return Convert.ToInt32(diskDrive["Index"]);
            }

            throw new InvalidOperationException(
                "Não foi possível determinar o disco físico que hospeda o Windows em execução.");
        }
    }
}
