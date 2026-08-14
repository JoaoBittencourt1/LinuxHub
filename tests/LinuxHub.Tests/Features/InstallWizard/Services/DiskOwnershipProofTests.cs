using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class DiskOwnershipProofTests
    {
        private static InstallationPlanDisk SampleDisk() => new()
        {
            Number = 0,
            UniqueId = "disk-unique",
            PartitionTableId = "gpt:11111111-1111-1111-1111-111111111111",
            SizeBytes = 512L * 1024 * 1024 * 1024,
            PartitionStyle = "GPT",
            Windows = new InstallationPlanPartitionIdentity
            {
                Number = 3,
                OffsetBytes = 1_048_576,
                SizeBytes = 200L * 1024 * 1024 * 1024,
            },
            Installer = new InstallationPlanInstallerPartition
            {
                Number = 5,
                OffsetBytes = 400L * 1024 * 1024 * 1024,
                StagingSizeBytes = 8L * 1024 * 1024 * 1024,
            },
        };

        [Fact]
        public void ProveDisk_AcceptsExactIdentityMatch()
        {
            var disk = SampleDisk();
            var observed = new ObservedDiskIdentity(
                disk.Number, disk.UniqueId, disk.PartitionTableId, disk.SizeBytes, disk.PartitionStyle);

            Assert.Equal(DiskOwnershipProofStatus.Proven, DiskOwnershipProof.ProveDisk(disk, observed).Status);
        }

        [Fact]
        public void ProveDisk_RejectsUniqueIdMismatch()
        {
            var disk = SampleDisk();
            var observed = new ObservedDiskIdentity(
                disk.Number, "other", disk.PartitionTableId, disk.SizeBytes, disk.PartitionStyle);

            Assert.Equal(
                DiskOwnershipProofStatus.DiskMismatch,
                DiskOwnershipProof.ProveDisk(disk, observed).Status);
        }

        [Fact]
        public void ProvePartition_RejectsSizeDivergence()
        {
            var expected = SampleDisk().Windows;
            var observed = new ObservedPartitionGeometry(expected.Number, expected.OffsetBytes, expected.SizeBytes + 1);

            Assert.Equal(
                DiskOwnershipProofStatus.PartitionMismatch,
                DiskOwnershipProof.ProvePartition(expected, observed).Status);
        }

        [Fact]
        public void ProveStagingPartition_AcceptsExactGeometry()
        {
            var installer = SampleDisk().Installer;
            var observed = new ObservedPartitionGeometry(
                installer.Number!.Value,
                installer.OffsetBytes!.Value,
                installer.StagingSizeBytes);

            Assert.Equal(
                DiskOwnershipProofStatus.Proven,
                DiskOwnershipProof.ProveStagingPartition(installer, observed).Status);
        }

        [Fact]
        public void ProvePartitionAtOffset_IgnoresCurrentSize()
        {
            var result = DiskOwnershipProof.ProvePartitionAtOffset(
                1_048_576,
                new ObservedPartitionGeometry(3, 1_048_576, 50L * 1024 * 1024 * 1024));

            Assert.Equal(DiskOwnershipProofStatus.Proven, result.Status);
        }
    }
}
