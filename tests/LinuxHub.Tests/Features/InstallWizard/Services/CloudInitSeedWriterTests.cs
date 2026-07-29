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
        /// No modo substituir nada cria espaço não alocado antes daqui, e num disco tomado
        /// pelo Windows o New-Partition falhava com "Not enough available capacity". O script
        /// tem que abrir o espaço sozinho antes de tentar criar.
        /// </summary>
        [Fact]
        public void CreateScript_MakesRoomBeforeCreatingWhenTheDiskIsFull()
        {
            string script = CloudInitSeedWriter.BuildCreateScript(diskIndex: 0);

            Assert.Contains("LargestFreeExtent", script);
            Assert.Contains("Resize-Partition", script);

            // A folga tem que ser verificada ANTES da criação, senão não serve pra nada.
            Assert.True(
                script.IndexOf("LargestFreeExtent", StringComparison.Ordinal)
                    < script.IndexOf("New-Partition", StringComparison.Ordinal));
        }

        /// <summary>
        /// Mesma barreira do DiskPartitioningService: encolher uma não-NTFS corta o filesystem
        /// sem mover o conteúdo. Aqui o alvo é escolhido pelo script, não pelo usuário, então
        /// o filtro é a única coisa que impede mirar numa ext4 de instalação anterior.
        /// </summary>
        [Fact]
        public void CreateScript_OnlyEverShrinksAnNtfsPartition()
        {
            string script = CloudInitSeedWriter.BuildCreateScript(diskIndex: 0);

            Assert.Contains("$vol.FileSystem -eq 'NTFS'", script);
            Assert.Contains("Get-PartitionSupportedSize", script);
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
