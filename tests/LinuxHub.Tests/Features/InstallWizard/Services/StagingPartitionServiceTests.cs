using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Cobre a montagem dos scripts — criar partição e copiar 6 GB de verdade exige elevação
    /// e UAC, que não existem em teste.
    /// </summary>
    public class StagingPartitionServiceTests
    {
        private static readonly StagingPartition Partition = new(0, 4, "ABCD1234-5678-90AB-CDEF-1234567890AB");

        [Fact]
        public void RequiredBytes_LeavesRoomBeyondTheIsoItself()
        {
            var service = new StagingPartitionService(new FakeIsoFileInfoProvider(0));
            const long isoSize = 6_655_619_072;

            long required = service.RequiredBytesFor(isoSize);

            // Uma partição do tamanho exato da ISO não a comporta: sobra metadado de NTFS e o
            // alinhamento de 1 MiB que o New-Partition aplica no início.
            Assert.True(required > isoSize);
        }

        /// <summary>
        /// A semente é criada logo depois da staging, no mesmo vão, e a folga dela é exatamente
        /// 1 MiB. Se a staging terminar no meio de um limite de alinhamento, o Windows come
        /// esse MiB ao criar a próxima partição e a semente não cabe — o mesmo
        /// "Not enough available capacity" que originou esta mudança inteira.
        /// </summary>
        [Theory]
        [InlineData(6_655_619_072)]  // Ubuntu 24.04.4, o caso real
        [InlineData(1)]
        [InlineData(1024 * 1024 - 1)]
        [InlineData(7_000_000_001)]
        public void RequiredBytes_IsAlwaysMebibyteAligned(long isoSize)
        {
            var service = new StagingPartitionService(new FakeIsoFileInfoProvider(0));

            Assert.Equal(0, service.RequiredBytesFor(isoSize) % (1024 * 1024));
        }

        [Fact]
        public void CreateScript_FormatsAsNtfs()
        {
            string script = StagingPartitionService.BuildCreateScript(diskIndex: 0, sizeInBytes: 7_000_000_000);

            // NTFS não é escolha estética: FAT32 tem teto de 4 GB por arquivo e a ISO tem 6,2 GB;
            // exFAT o casper não monta (is_supported_fs).
            Assert.Contains("-FileSystem NTFS", script);
            Assert.DoesNotContain("FAT32", script);
            Assert.DoesNotContain("exFAT", script);
        }

        [Fact]
        public void CreateScript_RemovesDriveLetterAfterFormat()
        {
            string script = StagingPartitionService.BuildCreateScript(diskIndex: 0, sizeInBytes: 7_000_000_000);

            // Sem isso a cópia falha com "Cannot assign multiple drive letters": Create
            // deixava a letra e Copy tentava Add-PartitionAccessPath de novo.
            Assert.Contains("Remove-PartitionAccessPath", script);
            Assert.True(
                script.IndexOf("Format-Volume", StringComparison.Ordinal)
                    < script.IndexOf("Remove-PartitionAccessPath", StringComparison.Ordinal));
        }

        [Fact]
        public void CreateScript_ReportsPartitionNumberAndGuid()
        {
            string script = StagingPartitionService.BuildCreateScript(diskIndex: 0, sizeInBytes: 7_000_000_000);

            // Sem o GUID não há como remover a partição depois com segurança: o instalador
            // reescreve a tabela e os números de partição mudam.
            Assert.Contains(".Guid", script);
            Assert.Contains("STAGING_OK:", script);
        }

        [Fact]
        public void CreateOutput_YieldsNumberAndNormalizedUuid()
        {
            StagingPartition parsed = StagingPartitionService.ParseCreateOutputOrThrow(
                diskIndex: 0, output: "STAGING_OK: 4 {abcd1234-5678-90ab-cdef-1234567890ab} E");

            Assert.Equal(4, parsed.PartitionNumber);
            Assert.Equal("ABCD1234-5678-90AB-CDEF-1234567890AB", parsed.PartitionUuid);
            Assert.Equal(0, parsed.DiskIndex);
        }

        [Fact]
        public void CreateOutput_MissingGuidIsAnError()
        {
            // Nunca silencioso: sem identidade a limpeza pós-instalação não teria como saber
            // qual partição apagar, e apagar por índice é exatamente o que não pode acontecer.
            var error = Assert.Throws<InvalidOperationException>(
                () => StagingPartitionService.ParseCreateOutputOrThrow(0, "STAGING_OK: 4"));

            Assert.Contains("STAGING_OK: 4", error.Message);
        }

        [Fact]
        public void CopyScript_VerifiesSizeAndDiscardsATruncatedCopy()
        {
            string script = StagingPartitionService.BuildCopyScript(Partition, @"C:\ISOs\ubuntu.iso", 6_655_619_072);

            Assert.Contains("6655619072", script);
            Assert.Contains("Remove-Item", script);
            Assert.Contains("throw", script);
        }

        [Fact]
        public void CopyScript_AlwaysUnmountsEvenWhenTheCopyFails()
        {
            string script = StagingPartitionService.BuildCopyScript(Partition, @"C:\ISOs\ubuntu.iso", 100);

            Assert.Contains("finally", script);
            Assert.Contains("Remove-PartitionAccessPath", script);
        }

        /// <summary>
        /// Crase não é escape em string verbatim do C#, então escrevê-la duplicada — o reflexo
        /// natural ao montar continuação de linha do PowerShell — produz DUAS crases no arquivo.
        /// O PowerShell lê isso como crase literal virando argumento, quebra o comando em
        /// statements soltos, e o parser dele ACEITA: o script roda e devolve resultado errado.
        /// Bug real, encontrado em BootSecurityService antes de sair. Validar sintaxe de
        /// verdade exigiria o SDK do PowerShell no projeto de teste; esta asserção cobre o
        /// erro concreto que já aconteceu, sem essa dependência.
        /// </summary>
        [Theory]
        [MemberData(nameof(AllScripts))]
        public void Scripts_UseNoBacktickLineContinuation(string script)
        {
            Assert.DoesNotContain("``", script);
        }

        /// <summary>
        /// Chave desbalanceada é o outro jeito de errar interpolação: num <c>$@""</c> cada
        /// chave literal precisa ser duplicada, e esquecer uma quebra o script inteiro.
        /// </summary>
        [Theory]
        [MemberData(nameof(AllScripts))]
        public void Scripts_HaveBalancedBraces(string script)
        {
            Assert.Equal(script.Count(c => c == '{'), script.Count(c => c == '}'));
        }

        public static TheoryData<string> AllScripts() => new()
        {
            StagingPartitionService.BuildCreateScript(0, 7_000_000_000),
            StagingPartitionService.BuildCopyScript(Partition, @"C:\ISOs\ubuntu.iso", 6_655_619_072),
            DiskPartitioningService.BuildEnsureSpaceScript(0, 7_000_000_000, 2),
            DiskPartitioningService.BuildScript(0, 3, 107_374_182_400, 2),
            CloudInitSeedWriter.BuildCreateScript(0),
        };

        private sealed class FakeIsoFileInfoProvider(long size) : IIsoFileInfoProvider
        {
            public long GetSizeInBytes(string isoPath) => size;
        }
    }
}
