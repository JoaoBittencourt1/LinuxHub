using System.Text;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Cobre só a montagem dos scripts — criar partição de verdade exige elevação e UAC.
    /// </summary>
    public class CloudInitSeedWriterTests
    {
        [Fact]
        public void CreateScript_FormatsAsFat32WithTheLabelCloudInitLooksFor()
        {
            string script = CloudInitSeedWriter.BuildCreateScript(diskIndex: 0);

            // O rótulo CIDATA é o que faz o cloud-init achar a semente sozinho, sem precisar
            // do parâmetro ds=nocloud na linha de comando do kernel.
            Assert.Contains("-FileSystem FAT32", script);
            Assert.Contains("-NewFileSystemLabel 'CIDATA'", script);
            Assert.Contains("New-Partition -DiskNumber 0", script);
        }

        /// <summary>
        /// Abrir espaço não é mais responsabilidade daqui: vive num ponto único
        /// (<c>DiskPartitioningService.EnsureUnallocatedSpace</c>) que soma de uma vez o que a
        /// staging e a semente precisam e executa UM shrink. Ter cada serviço encolhendo por
        /// conta própria significava dois resizes encadeados no mesmo volume.
        /// </summary>
        [Fact]
        public void CreateScript_DoesNotShrinkAnything()
        {
            string script = CloudInitSeedWriter.BuildCreateScript(diskIndex: 0);

            Assert.DoesNotContain("Resize-Partition", script);
            Assert.DoesNotContain("LargestFreeExtent", script);
        }

        /// <summary>A semente precisa entrar na conta de espaço de quem prepara o disco — o
        /// shrink é um só, e depois dele não há segunda chance de pedir mais.</summary>
        [Fact]
        public void RequiredBytes_CoversThePartitionPlusAlignment()
        {
            Assert.True(CloudInitSeedWriter.RequiredBytes > 128L * 1024 * 1024);
        }

        [Fact]
        public void CreateScript_LeavesNoDriveLetterBehind()
        {
            string script = CloudInitSeedWriter.BuildCreateScript(diskIndex: 0);

            Assert.Contains("Remove-PartitionAccessPath", script);
        }

        [Fact]
        public void WriteScript_CarriesContentAsBase64InsteadOfAHereString()
        {
            const string userData = "#cloud-config\nautoinstall:\n  version: 1\n";
            const string metaData = "instance-id: linuxhub-teste\n";

            string script = CloudInitSeedWriter.BuildWriteScript(0, 5, userData, metaData);

            // Here-string do PowerShell traria o CRLF do arquivo .ps1 para dentro do YAML e
            // Set-Content -Encoding UTF8 no PS 5.1 ainda escreveria um BOM, que quebra a
            // detecção do '#cloud-config' na primeira linha. Base64 preserva os bytes.
            Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(userData)), script);
            Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(metaData)), script);
            Assert.Contains("WriteAllBytes", script);
            Assert.DoesNotContain("@'", script);
            Assert.DoesNotContain("Set-Content", script);
        }

        [Fact]
        public void WriteScript_WritesBothFilesAtTheVolumeRoot()
        {
            string script = CloudInitSeedWriter.BuildWriteScript(0, 5, "a", "b");

            Assert.Contains(@"\user-data", script);
            Assert.Contains(@"\meta-data", script);
        }

        [Fact]
        public void WriteScript_AlwaysUnmountsEvenWhenTheWriteFails()
        {
            string script = CloudInitSeedWriter.BuildWriteScript(0, 5, "a", "b");

            Assert.Contains("finally", script);
        }

        [Fact]
        public void PartitionNumber_IsReadBackFromTheScriptOutput()
        {
            Assert.Equal(5, CloudInitSeedWriter.ExtractPartitionNumberOrThrow("SEED_OK: 5"));
            Assert.Equal(12, CloudInitSeedWriter.ExtractPartitionNumberOrThrow("ruído\nSEED_OK: 12\nmais ruído"));
        }

        [Fact]
        public void PartitionNumber_MissingFromOutputIsAnError()
        {
            // Nunca silencioso: sem o número a instalação não sabe o que declarar no
            // storage config nem o que limpar depois.
            var error = Assert.Throws<InvalidOperationException>(
                () => CloudInitSeedWriter.ExtractPartitionNumberOrThrow("nada aqui"));

            Assert.Contains("nada aqui", error.Message);
        }
    }
}
