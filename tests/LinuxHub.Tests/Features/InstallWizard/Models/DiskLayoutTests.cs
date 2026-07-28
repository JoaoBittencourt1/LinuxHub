using LinuxHub.Features.InstallWizard.Models;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Models
{
    public class DiskLayoutTests
    {
        private const long Gib = 1024L * 1024 * 1024;
        private const string BasicData = "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}";
        private const string EspType = "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}";

        private static DiskLayout Build(params PartitionLayout[] partitions) =>
            new(0, "SERIAL", "Disco", 500 * Gib, IsGpt: true, IsLargestDisk: true, IsSmallestDisk: true, partitions);

        [Fact]
        public void FindLargestFreeGap_FindsTheSpaceLeftAtTheEndByAShrink()
        {
            var disk = Build(
                new PartitionLayout(1, 1024 * 1024, 200L * 1024 * 1024, EspType, true),
                new PartitionLayout(3, 1 * Gib, 300 * Gib, BasicData, false));

            (long offset, long size) = disk.FindLargestFreeGap();

            Assert.Equal(301 * Gib, offset);
            // 1 MiB no fim fica reservado para a cópia de segurança da tabela GPT.
            Assert.Equal(500 * Gib - 301 * Gib - (1024 * 1024), size);
        }

        [Fact]
        public void FindLargestFreeGap_PrefersTheBiggestHoleNotTheFirst()
        {
            var disk = Build(
                new PartitionLayout(1, 10 * Gib, 10 * Gib, BasicData, false),
                new PartitionLayout(2, 100 * Gib, 300 * Gib, BasicData, false));

            (long offset, long size) = disk.FindLargestFreeGap();

            // Buracos: 0–10 GiB (10), 20–100 GiB (80), 400 GiB–fim (~100). O do fim vence.
            Assert.Equal(400 * Gib, offset);
            Assert.True(size > 80 * Gib);
        }

        [Fact]
        public void FindLargestFreeGap_ReturnsZeroOnAFullDisk()
        {
            var disk = Build(new PartitionLayout(1, 0, 500 * Gib, BasicData, false));

            Assert.Equal((0L, 0L), disk.FindLargestFreeGap());
        }

        [Fact]
        public void NextFreePartitionNumber_SkipsGapsInTheNumbering()
        {
            // Numeração com buraco é válida em GPT — Count + 1 devolveria um número já usado.
            var disk = Build(
                new PartitionLayout(1, 0, 1 * Gib, EspType, true),
                new PartitionLayout(4, 2 * Gib, 1 * Gib, BasicData, false));

            Assert.Equal(5, disk.NextFreePartitionNumber);
        }

        [Fact]
        public void EfiSystemPartition_IsFoundByTypeNotByPosition()
        {
            var disk = Build(
                new PartitionLayout(1, 0, 1 * Gib, BasicData, false),
                new PartitionLayout(2, 2 * Gib, 1 * Gib, EspType, true));

            Assert.Equal(2, disk.EfiSystemPartition!.Number);
        }
    }
}
