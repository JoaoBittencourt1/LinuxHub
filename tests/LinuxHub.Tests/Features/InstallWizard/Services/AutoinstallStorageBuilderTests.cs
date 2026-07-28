using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Esta é a única parte do autoinstall capaz de apagar o Windows do usuário: se uma
    /// partição existente sair da lista, ou sair sem <c>preserve: true</c>, o curtin a trata
    /// como espaço livre. Os testes abaixo existem para travar exatamente isso.
    ///
    /// O layout usado imita um disco real: ESP + partição reservada + Windows, e espaço não
    /// alocado no fim (o que o encolhimento libera). A partição 4 é a semente CIDATA — sem
    /// <c>Guid</c> por padrão, então os testes que não mexem nisso continuam exercitando o
    /// critério de tamanho como antes; os testes de identidade por PARTUUID atribuem um Guid a
    /// ela explicitamente.
    /// </summary>
    public class AutoinstallStorageBuilderTests
    {
        private const long Gib = 1024L * 1024 * 1024;
        private const int SeedPartitionNumber = 4;

        private static DiskLayout BuildDiskWithFreeSpace(long freeSpaceBytes = 100 * Gib)
        {
            long espOffset = 1024 * 1024;
            long espSize = 200L * 1024 * 1024;
            long reservedOffset = espOffset + espSize;
            long reservedSize = 16L * 1024 * 1024;
            long windowsOffset = reservedOffset + reservedSize;
            long windowsSize = 200 * Gib;
            long seedOffset = windowsOffset + windowsSize;
            long seedSize = 128L * 1024 * 1024;

            return new DiskLayout(
                Index: 0,
                SerialNumber: "SERIAL123",
                Model: "NVMe de teste",
                SizeBytes: seedOffset + seedSize + freeSpaceBytes + (1024 * 1024),
                IsGpt: true,
                IsLargestDisk: true,
                IsSmallestDisk: true,
                Partitions: new[]
                {
                    new PartitionLayout(1, espOffset, espSize, "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}", true),
                    new PartitionLayout(2, reservedOffset, reservedSize, "{e3c9e316-0b5c-4db8-817d-f92df00215ae}", false),
                    new PartitionLayout(3, windowsOffset, windowsSize, "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}", false),
                    new PartitionLayout(SeedPartitionNumber, seedOffset, seedSize, "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}", false)
                });
        }

        private static DiskLayout BuildMbrDiskWithFreeSpace(long freeSpaceBytes = 100 * Gib)
        {
            long systemOffset = 1024 * 1024;
            long systemSize = 100L * 1024 * 1024;
            long windowsOffset = systemOffset + systemSize;
            long windowsSize = 200 * Gib;

            return new DiskLayout(
                Index: 0,
                SerialNumber: "SERIAL123",
                Model: "HD de teste",
                SizeBytes: windowsOffset + windowsSize + freeSpaceBytes + (1024 * 1024),
                IsGpt: false,
                IsLargestDisk: true,
                IsSmallestDisk: true,
                Partitions: new[]
                {
                    new PartitionLayout(1, systemOffset, systemSize, "", false, MbrType: 0x07, IsActive: true),
                    new PartitionLayout(2, windowsOffset, windowsSize, "", false, MbrType: 0x07)
                });
        }

        private static DiskLayout WithSeedPartuuid(DiskLayout disk, string guid) => disk with
        {
            Partitions = disk.Partitions
                .Select(p => p.Number == SeedPartitionNumber ? p with { Guid = guid } : p)
                .ToList()
        };

        [Fact]
        public void DualBoot_DeclaresEveryExistingPartitionAsPreserved()
        {
            string yaml = AutoinstallStorageBuilder.Build(
                BuildDiskWithFreeSpace(), InstallMode.DualBoot, isUefi: true, indentSpaces: 4, SeedPartitionNumber);

            foreach (int number in new[] { 1, 2, 3, 4 })
                Assert.Contains($"id: partition-{number}", yaml);

            // 5 entradas de partição: as 4 existentes mais a raiz nova.
            Assert.Equal(5, CountOccurrences(yaml, "type: partition"));

            // 6 preserve: true: o disco, as 4 partições existentes e o format da ESP (que é
            // declarada como fat32 só para ser montada, nunca reformatada).
            Assert.Equal(6, CountOccurrences(yaml, "preserve: true"));
        }

        /// <summary>
        /// Regressão da instalação real que deixou o Windows sem bootar: <c>preserve: true</c>
        /// não preserva o TIPO da partição. O curtin recupera uuid/name/attrs de uma partição
        /// preservada, mas não o type — sem <c>partition_type</c> explícito o sfdisk carimba
        /// "Linux filesystem" na partição reservada e no C: do Windows, e o Windows Boot
        /// Manager para de reconhecer o volume.
        /// </summary>
        [Fact]
        public void DualBoot_DeclaresTheOriginalTypeOfEveryPreservedPartition()
        {
            string yaml = AutoinstallStorageBuilder.Build(
                BuildDiskWithFreeSpace(), InstallMode.DualBoot, isUefi: true, indentSpaces: 4, SeedPartitionNumber);

            // Sem chaves e em maiúsculas — a forma que o sfdisk aceita.
            Assert.Contains("partition_type: C12A7328-F81F-11D2-BA4B-00A0C93EC93B", yaml);
            Assert.Contains("partition_type: E3C9E316-0B5C-4DB8-817D-F92DF00215AE", yaml);
            Assert.Contains("partition_type: EBD0A0A2-B9E5-4433-87C0-68B6B72699C7", yaml);
            Assert.DoesNotContain("partition_type: {", yaml);

            // Toda partição declarada (4 existentes + a raiz nova) traz o seu tipo.
            Assert.Equal(5, CountOccurrences(yaml, "partition_type:"));

            // E só a raiz nova é Linux filesystem.
            Assert.Equal(1, CountOccurrences(yaml, "partition_type: 0FC63DAF-8483-4772-8E79-3D69D8477DE4"));
        }

        [Fact]
        public void DualBoot_RefusesAPartitionWhoseTypeTheWindowsDidNotReport()
        {
            // Omitir o campo seria voltar ao bug que apagou o boot do Windows: sem tipo, o
            // sfdisk aplica o padrão dele. Recusar é a única saída correta.
            var disk = BuildDiskWithFreeSpace();
            var disk3 = disk with
            {
                Partitions = disk.Partitions.Select(p => p.Number == 3 ? p with { GptType = "" } : p).ToList()
            };

            var error = Assert.Throws<InvalidOperationException>(
                () => AutoinstallStorageBuilder.Build(disk3, InstallMode.DualBoot, true, 4, SeedPartitionNumber));

            Assert.Contains("tipo GPT da partição 3", error.Message);
        }

        [Fact]
        public void DualBoot_OnAnMbrDiskKeepsTheWindowsTypeByteAndTheActiveFlag()
        {
            // Mesmo bug, tabela MBR: sem partition_type o sfdisk grava 0x83 por cima do 0x07 do
            // Windows, e sem flag: boot a partição perde o bit "ativa" que a BIOS procura.
            long systemOffset = 1024 * 1024;
            long systemSize = 100L * 1024 * 1024;
            long windowsOffset = systemOffset + systemSize;
            long windowsSize = 200 * Gib;

            var disk = new DiskLayout(
                Index: 0,
                SerialNumber: "SERIAL123",
                Model: "HD de teste",
                SizeBytes: windowsOffset + windowsSize + (100 * Gib) + (1024 * 1024),
                IsGpt: false,
                IsLargestDisk: true,
                IsSmallestDisk: true,
                Partitions: new[]
                {
                    new PartitionLayout(1, systemOffset, systemSize, "", false, MbrType: 0x07, IsActive: true),
                    new PartitionLayout(2, windowsOffset, windowsSize, "", false, MbrType: 0x07)
                });

            string yaml = AutoinstallStorageBuilder.Build(disk, InstallMode.DualBoot, isUefi: false, indentSpaces: 4, seedPartitionNumber: 2);

            // Com aspas: `0x07` cru seria o inteiro 7 depois do parse do YAML.
            Assert.Equal(2, CountOccurrences(yaml, "partition_type: '0x07'"));
            Assert.Equal(1, CountOccurrences(yaml, "partition_type: '0x83'"));
            Assert.Equal(1, CountOccurrences(yaml, "flag: boot"));
        }

        [Fact]
        public void DualBoot_NeverReformatsTheEfiSystemPartition()
        {
            string yaml = AutoinstallStorageBuilder.Build(
                BuildDiskWithFreeSpace(), InstallMode.DualBoot, isUefi: true, indentSpaces: 4, SeedPartitionNumber);

            // A ESP precisa ser declarada como fat32 para o curtin montá-la em /boot/efi e
            // instalar o GRUB, mas COM preserve: true — sem isso ela é reformatada e o
            // bootloader do Windows, que mora nela, vai junto.
            int espFormat = yaml.IndexOf("id: format-esp", StringComparison.Ordinal);
            Assert.True(espFormat > 0, "a ESP precisa ter uma entrada de format");

            string espBlock = yaml[espFormat..];
            Assert.Contains("fstype: fat32", espBlock[..120]);
            Assert.Contains("preserve: true", espBlock[..120]);
        }

        [Fact]
        public void DualBoot_PutsGrubOnTheEspInUefiAndOnTheDiskInBios()
        {
            string uefi = AutoinstallStorageBuilder.Build(
                BuildDiskWithFreeSpace(), InstallMode.DualBoot, isUefi: true, indentSpaces: 4, SeedPartitionNumber);
            string bios = AutoinstallStorageBuilder.Build(
                BuildDiskWithFreeSpace(), InstallMode.DualBoot, isUefi: false, indentSpaces: 4, SeedPartitionNumber);

            // Em UEFI o grub_device é a ESP; em BIOS legado é o disco (GRUB vai pro MBR).
            Assert.Equal(1, CountOccurrences(uefi, "grub_device: true"));
            Assert.Equal(1, CountOccurrences(bios, "grub_device: true"));

            int uefiDisk = uefi.IndexOf("type: disk", StringComparison.Ordinal);
            int uefiEsp = uefi.IndexOf("id: partition-1", StringComparison.Ordinal);
            Assert.True(
                uefi.IndexOf("grub_device: true", StringComparison.Ordinal) > uefiEsp,
                "em UEFI a flag pertence à ESP, não ao disco");
            Assert.Contains("grub_device: false", uefi[uefiDisk..uefiEsp]);
        }

        [Fact]
        public void DualBoot_CreatesRootInTheFreeSpaceWithTheNextPartitionNumber()
        {
            string yaml = AutoinstallStorageBuilder.Build(
                BuildDiskWithFreeSpace(), InstallMode.DualBoot, isUefi: true, indentSpaces: 4, SeedPartitionNumber);

            Assert.Contains("id: partition-5", yaml);
            Assert.Contains("number: 5", yaml);
            Assert.Contains("wipe: superblock", yaml);
            Assert.Contains("fstype: ext4", yaml);
            Assert.Contains("path: /", yaml);
            Assert.Contains("path: /boot/efi", yaml);
        }

        [Fact]
        public void DualBoot_AlignsTheNewPartitionToOneMebibyte()
        {
            // Um offset desalinhado custa desempenho em SSD e algumas firmwares recusam.
            var disk = BuildDiskWithFreeSpace();
            string yaml = AutoinstallStorageBuilder.Build(disk, InstallMode.DualBoot, true, 4, SeedPartitionNumber);

            (long gapOffset, _) = disk.FindLargestFreeGap();
            long expected = (gapOffset + (1024 * 1024) - 1) / (1024 * 1024) * (1024 * 1024);

            Assert.Contains($"offset: {expected}", yaml);
        }

        [Fact]
        public void DualBoot_RefusesWhenTheFreeSpaceIsTooSmallForASystem()
        {
            var disk = BuildDiskWithFreeSpace(freeSpaceBytes: 4 * Gib);

            var error = Assert.Throws<InvalidOperationException>(
                () => AutoinstallStorageBuilder.Build(disk, InstallMode.DualBoot, true, 4, SeedPartitionNumber));

            Assert.Contains("espaço não alocado", error.Message);
        }

        [Fact]
        public void DualBoot_RefusesUefiWithoutAnEfiSystemPartition()
        {
            var disk = BuildDiskWithFreeSpace() with
            {
                Partitions = new[]
                {
                    new PartitionLayout(3, 1024 * 1024, 200 * Gib, "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}", false)
                }
            };

            var error = Assert.Throws<InvalidOperationException>(
                () => AutoinstallStorageBuilder.Build(disk, InstallMode.DualBoot, true, 4, SeedPartitionNumber));

            Assert.Contains("EFI System Partition", error.Message);
        }

        [Fact]
        public void DualBoot_RefusesUefiOnAnMbrDisk()
        {
            // Converter a tabela de partição para GPT apagaria o Windows — recusar é a única
            // saída correta.
            var disk = BuildDiskWithFreeSpace() with { IsGpt = false };

            var error = Assert.Throws<InvalidOperationException>(
                () => AutoinstallStorageBuilder.Build(disk, InstallMode.DualBoot, true, 4, SeedPartitionNumber));

            Assert.Contains("GPT", error.Message);
        }

        [Fact]
        public void Replace_DelegatesToTheSubiquityDirectLayout()
        {
            string yaml = AutoinstallStorageBuilder.Build(
                BuildDiskWithFreeSpace(), InstallMode.Replace, isUefi: true, indentSpaces: 4, SeedPartitionNumber);

            Assert.Contains("name: direct", yaml);

            // Nenhuma partição é enumerada no modo substituir — o disco inteiro é do Linux.
            Assert.DoesNotContain("preserve: true", yaml);
        }

        [Fact]
        public void DiskMatch_NeverUsesTheWindowsSerialNumber()
        {
            // Regressão de uma instalação real que morreu com "matched no disk": num NVMe o
            // Windows reporta o EUI-64 do namespace e o Linux o serial do controlador, que
            // são campos diferentes e nunca coincidem. Sem PARTUUID na semente, cai no
            // critério de tamanho — que também nunca usa o serial do Windows.
            var disk = BuildDiskWithFreeSpace() with
            {
                SerialNumber = "0000_0000_0000_0000_6D1C_0035_0218_7C70."
            };

            foreach (InstallMode mode in new[] { InstallMode.DualBoot, InstallMode.Replace })
            {
                string yaml = AutoinstallStorageBuilder.Build(disk, mode, isUefi: true, indentSpaces: 4, SeedPartitionNumber);

                Assert.DoesNotContain("serial:", yaml);
                Assert.DoesNotContain("6D1C", yaml);
                Assert.Contains("size: largest", yaml);
            }
        }

        [Fact]
        public void DiskMatch_WorksOnMultiDiskMachinesWhenTheTargetIsTheLargestOrSmallest()
        {
            // `size` compara número, não texto — funciona com quantos discos existirem,
            // desde que o alvo seja um dos extremos.
            var largest = BuildDiskWithFreeSpace() with { IsLargestDisk = true, IsSmallestDisk = false };
            var smallest = BuildDiskWithFreeSpace() with { IsLargestDisk = false, IsSmallestDisk = true };

            Assert.Contains(
                "size: largest",
                AutoinstallStorageBuilder.Build(largest, InstallMode.DualBoot, true, 4, SeedPartitionNumber));
            Assert.Contains(
                "size: smallest",
                AutoinstallStorageBuilder.Build(smallest, InstallMode.DualBoot, true, 4, SeedPartitionNumber));
        }

        [Fact]
        public void DiskMatch_RefusesADiskThatIsNeitherTheLargestNorTheSmallest()
        {
            // Disco do meio, ou empate de tamanho no extremo, SEM PARTUUID disponível: não há
            // critério correto disponível, e um match errado apagaria o disco errado. "Não sei
            // escolher" é o único resultado aceitável.
            var disk = BuildDiskWithFreeSpace() with { IsLargestDisk = false, IsSmallestDisk = false };

            var error = Assert.Throws<InvalidOperationException>(
                () => AutoinstallStorageBuilder.Build(disk, InstallMode.DualBoot, true, 4, SeedPartitionNumber));

            Assert.Contains("maior ou o menor", error.Message);
        }

        [Fact]
        public void DiskMatch_NeverAddsModelAsAnExtraFilter()
        {
            // Parece mais seguro e é o oposto: uma divergência de um caractere entre a string
            // do Windows e a do Linux transforma um match que funcionava em "matched no disk".
            string yaml = AutoinstallStorageBuilder.Build(
                BuildDiskWithFreeSpace(), InstallMode.DualBoot, true, 4, SeedPartitionNumber);

            Assert.DoesNotContain("model:", yaml);
            Assert.DoesNotContain("NVMe de teste", yaml);
        }

        [Fact]
        public void DiskMatch_IdentifiesTheMiddleDiskByThePartuuidOfTheSeedPartition()
        {
            // O caso que o critério de tamanho recusa (disco do meio) passa a funcionar quando
            // a semente CIDATA tem um GUID GPT conhecido — a identidade não depende de ranking.
            var disk = WithSeedPartuuid(
                BuildDiskWithFreeSpace() with { IsLargestDisk = false, IsSmallestDisk = false },
                "{6a1e2c3d-1111-2222-3333-444455556666}");

            string yaml = AutoinstallStorageBuilder.Build(disk, InstallMode.DualBoot, true, 4, SeedPartitionNumber);

            Assert.Contains($"path: {EarlyCommandsBuilder.DiskPathPlaceholder}", yaml);
            Assert.DoesNotContain("size: largest", yaml);
            Assert.DoesNotContain("size: smallest", yaml);
        }

        [Fact]
        public void DiskMatch_IdentifiesATieOfSizeByThePartuuidOfTheSeedPartition()
        {
            var disk = WithSeedPartuuid(
                BuildDiskWithFreeSpace() with { IsLargestDisk = false, IsSmallestDisk = false },
                "{6a1e2c3d-1111-2222-3333-444455556666}");

            string yaml = AutoinstallStorageBuilder.Build(disk, InstallMode.Replace, true, 4, SeedPartitionNumber);

            Assert.Contains($"path: {EarlyCommandsBuilder.DiskPathPlaceholder}", yaml);
        }

        [Fact]
        public void EarlyCommands_ResolvesTheDiskByThePartuuidOfTheSeedPartitionOnGpt()
        {
            var disk = WithSeedPartuuid(BuildDiskWithFreeSpace(), "{6A1E2C3D-1111-2222-3333-444455556666}");

            string? script = AutoinstallStorageBuilder.BuildEarlyCommands(disk, SeedPartitionNumber, indentSpaces: 4);

            Assert.NotNull(script);
            Assert.Contains("blkid -t PARTUUID=\"6a1e2c3d-1111-2222-3333-444455556666\"", script);
            Assert.Contains("lsblk -no pkname", script);
            Assert.Contains($"sed -i \"s|{EarlyCommandsBuilder.DiskPathPlaceholder}|$disk|\" /autoinstall.yaml", script);
        }

        [Fact]
        public void EarlyCommands_IsAbsentWhenTheDiskIsIdentifiedBySizeOnly()
        {
            string? script = AutoinstallStorageBuilder.BuildEarlyCommands(BuildDiskWithFreeSpace(), SeedPartitionNumber, indentSpaces: 4);

            Assert.Null(script);
        }

        [Fact]
        public void DiskMatch_IdentifiesTheMiddleDiskByTheMbrSignatureWhenUnique()
        {
            var disk = BuildMbrDiskWithFreeSpace() with
            {
                IsLargestDisk = false,
                IsSmallestDisk = false,
                DiskSignatureHex = "1a2b3c4d",
                HasUniqueDiskSignature = true
            };

            string yaml = AutoinstallStorageBuilder.Build(disk, InstallMode.DualBoot, false, 4, seedPartitionNumber: 2);

            Assert.Contains($"path: {EarlyCommandsBuilder.DiskPathPlaceholder}", yaml);
        }

        [Fact]
        public void EarlyCommands_ResolvesTheDiskByTheMbrSignature()
        {
            var disk = BuildMbrDiskWithFreeSpace() with
            {
                DiskSignatureHex = "1a2b3c4d",
                HasUniqueDiskSignature = true
            };

            string? script = AutoinstallStorageBuilder.BuildEarlyCommands(disk, seedPartitionNumber: 2, indentSpaces: 4);

            Assert.NotNull(script);
            Assert.Contains("blkid -t PTUUID=\"1a2b3c4d\"", script);
            Assert.DoesNotContain("lsblk -no pkname", script);
        }

        [Fact]
        public void DiskMatch_FallsBackToSizeWhenTheMbrSignatureIsZeroed()
        {
            // Assinatura 0x00000000: disco nunca inicializado por um Windows específico — não
            // é um identificador utilizável (DiskLayoutProvider já devolve string vazia nesse
            // caso, mas o teste trava o comportamento do lado do builder também).
            var disk = BuildMbrDiskWithFreeSpace() with
            {
                DiskSignatureHex = "",
                HasUniqueDiskSignature = false
            };

            string yaml = AutoinstallStorageBuilder.Build(disk, InstallMode.DualBoot, false, 4, seedPartitionNumber: 2);

            Assert.Contains("size: largest", yaml);
        }

        [Fact]
        public void DiskMatch_FallsBackToSizeWhenTheMbrSignatureIsDuplicatedAcrossDisks()
        {
            // HasUniqueDiskSignature=false é como o DiskLayoutProvider sinaliza colisão (ex.:
            // discos clonados por imagem) — mesmo com uma assinatura não-zero presente, ela não
            // pode ser usada como identidade.
            var disk = BuildMbrDiskWithFreeSpace() with
            {
                DiskSignatureHex = "1a2b3c4d",
                HasUniqueDiskSignature = false
            };

            string yaml = AutoinstallStorageBuilder.Build(disk, InstallMode.DualBoot, false, 4, seedPartitionNumber: 2);

            Assert.Contains("size: largest", yaml);
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int index = 0;

            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }
    }
}
