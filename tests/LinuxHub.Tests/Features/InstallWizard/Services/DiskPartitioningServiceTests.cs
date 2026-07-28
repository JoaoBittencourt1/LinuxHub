using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class DiskPartitioningServiceTests
    {
        [Fact]
        public void BuildScript_TargetsSelectedDiskAndPartition()
        {
            string script = DiskPartitioningService.BuildScript(diskIndex: 1, partitionIndex: 4, sizeInGb: 50);

            Assert.Contains("-DiskNumber 1 -PartitionNumber 4", script);
            Assert.Contains("Resize-Partition -DiskNumber 1 -PartitionNumber 4", script);
            Assert.Contains("$partition.Size - 50GB", script);
        }

        [Fact]
        public void BuildScript_NeverCreatesPartitionOrAssignsLetter()
        {
            string script = DiskPartitioningService.BuildScript(diskIndex: 0, partitionIndex: 2, sizeInGb: 20);

            Assert.DoesNotContain("New-Partition", script);
            Assert.DoesNotContain("Set-Partition", script);
            Assert.DoesNotContain("Format-Volume", script);
            Assert.DoesNotContain("Remove-Partition", script);
        }

        [Fact]
        public void BuildScript_DoesNotDependOnDiskpartVolumeFocus()
        {
            // Bug real: `select partition N` + `shrink` falhava com "Não há volume em foco",
            // porque o shrink do diskpart age sobre o volume, não sobre a partição — e uma
            // partição sem volume (MSR) é selecionável por número, então o alvo podia estar
            // errado sem ninguém perceber.
            string script = DiskPartitioningService.BuildScript(diskIndex: 0, partitionIndex: 2, sizeInGb: 20);

            Assert.DoesNotContain("select partition", script);
            Assert.DoesNotContain("select disk", script);
            Assert.DoesNotContain("shrink desired", script);
        }

        [Fact]
        public void BuildScript_ValidatesAgainstSupportedSizeBeforeResizing()
        {
            string script = DiskPartitioningService.BuildScript(diskIndex: 0, partitionIndex: 3, sizeInGb: 40);

            Assert.Contains("Get-PartitionSupportedSize", script);
            Assert.True(
                script.IndexOf("Get-PartitionSupportedSize", StringComparison.Ordinal)
                    < script.IndexOf("Resize-Partition", StringComparison.Ordinal),
                "a validação precisa rodar ANTES do resize, senão o erro só aparece depois de mexer no disco.");
        }
    }
}
