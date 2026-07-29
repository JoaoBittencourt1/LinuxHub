using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class DiskPartitioningServiceTests
    {
        [Fact]
        public void BuildScript_TargetsSelectedDiskAndPartition()
        {
            string script = DiskPartitioningService.BuildScript(diskIndex: 1, partitionIndex: 4, bytesToFree: 50L * 1024 * 1024 * 1024, newPartitionsPlanned: 2);

            Assert.Contains("-DiskNumber 1 -PartitionNumber 4", script);
            Assert.Contains("Resize-Partition -DiskNumber 1 -PartitionNumber 4", script);

            // Em bytes, não em GB: o encolhimento do dual-boot soma o que o usuário pediu no
            // slider (múltiplo de GB) com o que a staging e a semente consomem (MB).
            Assert.Contains($"$partition.Size - {50L * 1024 * 1024 * 1024}", script);
        }

        /// <summary>
        /// No modo substituir o usuário escolhe um DISCO, não uma partição — não há alvo de
        /// encolhimento informado, e o disco cheio de Windows não tem onde acomodar a staging.
        /// Este script é o único ponto que abre esse espaço.
        /// </summary>
        [Fact]
        public void EnsureSpaceScript_ShrinksTheLargestNtfsWhenThereIsNoRoom()
        {
            string script = DiskPartitioningService.BuildEnsureSpaceScript(diskIndex: 0, requiredBytes: 7_000_000_000, newPartitionsPlanned: 2);

            Assert.Contains("LargestFreeExtent", script);
            Assert.Contains("$vol.FileSystem -eq 'NTFS'", script);
            Assert.Contains("Get-PartitionSupportedSize", script);
            Assert.Contains("Resize-Partition", script);
        }

        /// <summary>
        /// Sair sem fazer nada quando já há espaço é o que protege o dual-boot: lá o
        /// encolhimento do slider já abriu o vão, e encolher de novo roubaria espaço do
        /// usuário sem ele pedir.
        /// </summary>
        [Fact]
        public void EnsureSpaceScript_DoesNothingWhenTheSpaceAlreadyExists()
        {
            string script = DiskPartitioningService.BuildEnsureSpaceScript(diskIndex: 0, requiredBytes: 7_000_000_000, newPartitionsPlanned: 2);

            Assert.True(
                script.IndexOf("LargestFreeExtent", StringComparison.Ordinal)
                    < script.IndexOf("Resize-Partition", StringComparison.Ordinal),
                "a checagem de espaço precisa vir ANTES do resize, senão ele roda sempre.");
            Assert.Contains("return", script);
        }

        /// <summary>
        /// MBR admite no máximo 4 partições e o preparo cria até três (raiz, staging, semente).
        /// Sem esta guarda o estouro só aparecia no <c>New-Partition</c>, com o disco já
        /// encolhido e a mensagem crua do Windows. Precisa vir antes de qualquer escrita.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Scripts_RefuseAnMbrDiskWithoutRoomBeforeWriting(int newPartitions)
        {
            string shrink = DiskPartitioningService.BuildScript(0, 3, 1024, newPartitions);
            string ensure = DiskPartitioningService.BuildEnsureSpaceScript(0, 1024, newPartitions);

            foreach (string script in new[] { shrink, ensure })
            {
                Assert.Contains("PartitionStyle", script);
                Assert.Contains($"+ {newPartitions}) -gt 4", script);

                Assert.True(
                    script.IndexOf("PartitionStyle", StringComparison.Ordinal)
                        < script.IndexOf("Resize-Partition", StringComparison.Ordinal),
                    "a guarda precisa vir ANTES do resize, senão o disco já foi alterado.");
            }
        }

        /// <summary>Em GPT o teto é 128 e o preparo nunca chega perto — a guarda não pode
        /// recusar nada lá.</summary>
        [Fact]
        public void PartitionSlotGuard_OnlyAppliesToMbr()
        {
            string script = DiskPartitioningService.BuildPartitionSlotGuard(0, 3);

            Assert.Contains("$estilo -eq 'MBR'", script);
        }

        [Fact]
        public void BuildScript_NeverCreatesPartitionOrAssignsLetter()
        {
            string script = DiskPartitioningService.BuildScript(diskIndex: 0, partitionIndex: 2, bytesToFree: 20L * 1024 * 1024 * 1024, newPartitionsPlanned: 2);

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
            string script = DiskPartitioningService.BuildScript(diskIndex: 0, partitionIndex: 2, bytesToFree: 20L * 1024 * 1024 * 1024, newPartitionsPlanned: 2);

            Assert.DoesNotContain("select partition", script);
            Assert.DoesNotContain("select disk", script);
            Assert.DoesNotContain("shrink desired", script);
        }

        [Fact]
        public void BuildScript_ValidatesAgainstSupportedSizeBeforeResizing()
        {
            string script = DiskPartitioningService.BuildScript(diskIndex: 0, partitionIndex: 3, bytesToFree: 40L * 1024 * 1024 * 1024, newPartitionsPlanned: 2);

            Assert.Contains("Get-PartitionSupportedSize", script);
            Assert.True(
                script.IndexOf("Get-PartitionSupportedSize", StringComparison.Ordinal)
                    < script.IndexOf("Resize-Partition", StringComparison.Ordinal),
                "a validação precisa rodar ANTES do resize, senão o erro só aparece depois de mexer no disco.");
        }
    }
}
