using System.IO;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class BootStagingServiceTests
    {
        private sealed class FakeEspLocator : IEspLocatorService
        {
            public int? FindEfiSystemPartitionIndex(int diskIndex) => null;
            public EfiSystemPartitionLocation? FindSystemEfiSystemPartition() => null;
        }

        private sealed class FakeGrubAssets : IGrubAssetProvider
        {
            public string GetUefiBootloaderPath() => "uefi.efi";
            public string GetBiosBootSectorPath() => "boot.img";
            public string GetBiosCoreImagePath() => "core.img";
        }

        private sealed class FakeMbrBackup : IMbrBackupService
        {
            public byte[] MbrToReturn { get; set; } = new byte[512];
            public string BackupMbr(int diskIndex, string backupPath) => backupPath;
            public void WriteBootCode(int diskIndex, string bootCodeFilePath) { }
            public void RestoreMbr(int diskIndex, string backupPath) { }
            public byte[] ReadMbr(int diskIndex) => MbrToReturn;
            public void WriteCoreImageToGap(int diskIndex, string coreImageFilePath) { }
        }

        private sealed class FakeBootConfiguration : IBootConfigurationService
        {
            public string AddFirmwareBootEntry(string description, char driveLetter, string efiPathOnVolume) => "{guid}";
        }

        private sealed class FakeIsoHostPartitionLocator : IIsoHostPartitionLocator
        {
            public string GetPartitionUuid(string windowsPath) => "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        }

        /// <summary>Os construtores reais: são classes puras de texto, sem I/O nem elevação,
        /// então substituí-los por fake só esconderia o arquivo que de fato é gerado.</summary>
        private static readonly IIsoBootEntryBuilderRegistry IsoBootEntryBuilders =
            new IsoBootEntryBuilderRegistry(
            [
                CasperIsoBootEntryBuilder.Instance,
                ArchisoIsoBootEntryBuilder.Instance,
            ]);

        private static byte[] BuildMbrWithFirstPartitionAt(uint startLba)
        {
            var mbr = new byte[512];
            mbr[446 + 4] = 0x07;
            BitConverter.GetBytes(startLba).CopyTo(mbr, 446 + 8);
            return mbr;
        }

        [Fact]
        public void EnsurePostMbrGapFitsCoreImage_ModernAlignment_DoesNotThrow()
        {
            string coreImage = Path.GetTempFileName();
            File.WriteAllBytes(coreImage, new byte[150_000]); // ~293 sectors, well under 2047

            var mbrBackup = new FakeMbrBackup { MbrToReturn = BuildMbrWithFirstPartitionAt(2048) };
            var service = new BootStagingService(new FakeEspLocator(), new FakeGrubAssets(), mbrBackup, new FakeBootConfiguration(), IsoBootEntryBuilders, new FakeIsoHostPartitionLocator());

            service.EnsurePostMbrGapFitsCoreImage(diskIndex: 0, coreImage);

            File.Delete(coreImage);
        }

        [Fact]
        public void EnsurePostMbrGapFitsCoreImage_TinyLegacyGap_ThrowsBeforeAnyWrite()
        {
            string coreImage = Path.GetTempFileName();
            File.WriteAllBytes(coreImage, new byte[150_000]);

            // Alinhamento pré-Vista: partição em LBA 63 (~31KB de gap, insuficiente).
            var mbrBackup = new FakeMbrBackup { MbrToReturn = BuildMbrWithFirstPartitionAt(63) };
            var service = new BootStagingService(new FakeEspLocator(), new FakeGrubAssets(), mbrBackup, new FakeBootConfiguration(), IsoBootEntryBuilders, new FakeIsoHostPartitionLocator());

            Assert.Throws<InvalidOperationException>(() => service.EnsurePostMbrGapFitsCoreImage(diskIndex: 0, coreImage));

            File.Delete(coreImage);
        }

        /// <summary>
        /// Bug real: no modo substituir IsoPath é um caminho GRUB dentro da staging
        /// ("/linuxhub.iso"), que nunca existe no sistema de arquivos do Windows.
        /// InstallStagingBootloader tratava esse caminho como se fosse do Windows e recusava
        /// TODA instalação em modo substituir com "ISO de staging não encontrada". A
        /// integridade da cópia já foi conferida em StagingPartitionService.CopyIso — não há
        /// nada a checar aqui de novo.
        /// </summary>
        [Fact]
        public void InstallStagingBootloader_AcceptsAGrubPathThatIsNotFullyQualified()
        {
            var service = new BootStagingService(
                new FakeEspLocator(), new FakeGrubAssets(), new FakeMbrBackup(), new FakeBootConfiguration(), IsoBootEntryBuilders, new FakeIsoHostPartitionLocator());

            var request = new BootStagingRequest(
                DistroName: "Ubuntu",
                IsoPath: "/linuxhub.iso",
                IsUefi: true,
                TargetDiskIndex: 0);

            // FakeEspLocator devolve null de propósito: se a exceção for sobre a ESP, o guard
            // do caminho da ISO passou. Se fosse sobre a ISO, o bug real ainda estaria lá.
            var error = Assert.Throws<InvalidOperationException>(() => service.InstallStagingBootloader(request));
            Assert.Contains("EFI System Partition", error.Message);
        }

        /// <summary>No dual-boot IsoPath é o caminho real da ISO no Windows (nunca copiada) —
        /// aqui a checagem continua valendo: se o arquivo sumiu depois de selecionado no
        /// wizard, é melhor descobrir antes de mexer no disco.</summary>
        [Fact]
        public void InstallStagingBootloader_DualBoot_StillRequiresTheOriginalIsoToExist()
        {
            var service = new BootStagingService(
                new FakeEspLocator(), new FakeGrubAssets(), new FakeMbrBackup(), new FakeBootConfiguration(), IsoBootEntryBuilders, new FakeIsoHostPartitionLocator());

            string missingPath = Path.Combine(Path.GetTempPath(), $"linuxhub-missing-{Guid.NewGuid():N}.iso");
            var request = new BootStagingRequest(
                DistroName: "Ubuntu",
                IsoPath: missingPath,
                IsUefi: true,
                TargetDiskIndex: 0);

            var error = Assert.Throws<InvalidOperationException>(() => service.InstallStagingBootloader(request));
            Assert.Contains(missingPath, error.Message);
        }

        [Fact]
        public void BuildEspStagingScript_MountsAndUnmountsTheSameAccessPath()
        {
            string script = BootStagingService.BuildEspStagingScript(
                diskIndex: 0, partitionIndex: 1, driveLetter: 'S',
                grubEfiSourcePath: @"C:\App\Assets\Grub\uefi\grubx64.efi",
                grubCfgContent: "menuentry \"x\" {}\n");

            Assert.Contains("Add-PartitionAccessPath -DiskNumber 0 -PartitionNumber 1 -AccessPath 'S:\\'", script);
            Assert.Contains("Remove-PartitionAccessPath -DiskNumber 0 -PartitionNumber 1 -AccessPath 'S:\\'", script);
            Assert.Contains(@"S:\EFI\linuxhub\grubx64.efi", script);
            Assert.Contains(@"S:\EFI\linuxhub\grub.cfg", script);
        }

        [Fact]
        public void BuildEspStagingAndBcdScript_RegistersBcdEntryBeforeUnmountingEsp()
        {
            // Bug real encontrado em teste: bcdedit falhava com "dispositivo não é válido
            // como especificado" porque a ESP já tinha sido desmontada antes do bcdedit
            // rodar (eram duas execuções elevadas separadas). Precisa ser uma só, com o
            // bcdedit dentro do mesmo try, antes do Remove-PartitionAccessPath.
            string script = BootStagingService.BuildEspStagingAndBcdScript(
                diskIndex: 0, partitionIndex: 1, driveLetter: 'S',
                grubEfiSourcePath: @"C:\App\Assets\Grub\uefi\grubx64.efi",
                grubCfgContent: "menuentry \"x\" {}\n",
                description: "Ubuntu (LinuxHub staging)",
                efiPathOnVolume: @"\EFI\linuxhub\grubx64.efi");

            int bcdCopyIndex = script.IndexOf("bcdedit /copy", StringComparison.Ordinal);
            int removeAccessPathIndex = script.IndexOf("Remove-PartitionAccessPath", StringComparison.Ordinal);

            Assert.True(bcdCopyIndex >= 0, "bcdedit /copy deveria estar no script.");
            Assert.True(removeAccessPathIndex >= 0, "Remove-PartitionAccessPath deveria estar no script.");
            Assert.True(bcdCopyIndex < removeAccessPathIndex,
                "bcdedit precisa rodar ANTES de desmontar a ESP, senão 'device partition=S:' falha.");

            Assert.Contains("device partition=S:", script);
            Assert.Contains(@"path '\EFI\linuxhub\grubx64.efi'", script);
        }

        [Fact]
        public void BuildGrubCfgWriteScript_WritesUnderSystemDriveBootGrub()
        {
            string script = BootStagingService.BuildGrubCfgWriteScript(@"C:\", "set timeout=10\n");

            Assert.Contains(@"C:\boot\grub", script);
            Assert.Contains("set timeout=10", script);
        }

        [Fact]
        public void PickFreeDriveLetter_NeverReturnsALetterAlreadyInUse()
        {
            char letter = BootStagingService.PickFreeDriveLetter();
            var usedLetters = System.IO.DriveInfo.GetDrives()
                .Select(d => char.ToUpperInvariant(d.Name[0]))
                .ToHashSet();

            Assert.DoesNotContain(letter, usedLetters);
        }
    }
}
