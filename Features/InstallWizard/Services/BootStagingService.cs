using System.IO;
using System.Linq;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed class BootStagingService : IBootStagingService
    {
        private readonly IEspLocatorService _espLocator;
        private readonly IGrubAssetProvider _grubAssets;
        private readonly IMbrBackupService _mbrBackup;
        private readonly IBootConfigurationService _bootConfiguration;

        public BootStagingService(
            IEspLocatorService espLocator,
            IGrubAssetProvider grubAssets,
            IMbrBackupService mbrBackup,
            IBootConfigurationService bootConfiguration)
        {
            _espLocator = espLocator ?? throw new ArgumentNullException(nameof(espLocator));
            _grubAssets = grubAssets ?? throw new ArgumentNullException(nameof(grubAssets));
            _mbrBackup = mbrBackup ?? throw new ArgumentNullException(nameof(mbrBackup));
            _bootConfiguration = bootConfiguration ?? throw new ArgumentNullException(nameof(bootConfiguration));
        }

        public void InstallStagingBootloader(BootStagingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            // No modo substituir, IsoPath é um caminho GRUB dentro da partição de staging
            // (ex.: "/linuxhub.iso") — nunca existiu no sistema de arquivos do Windows, e não
            // precisa: a integridade já foi conferida na cópia (StagingPartitionService.CopyIso
            // compara o tamanho copiado com o original). Confirmar isso com File.Exists tratando
            // o caminho como se fosse do Windows é o bug real que gerou esta guarda: o .NET
            // resolve "/linuxhub.iso" relativo à unidade atual (ex. C:\linuxhub.iso), nunca
            // encontra, e a instalação abortava sempre no modo substituir.
            //
            // No dual-boot, IsoPath É um caminho real do Windows (a ISO original, nunca
            // copiada) — `Path.IsPathFullyQualified` só é true nesse caso, e é aí que vale
            // conferir: a ISO pode ter sido movida ou apagada depois de selecionada no wizard.
            if (Path.IsPathFullyQualified(request.IsoPath) && !File.Exists(request.IsoPath))
            {
                throw new InvalidOperationException(
                    $"A ISO não foi encontrada em '{request.IsoPath}'. Ela pode ter sido movida " +
                    "ou apagada depois de selecionada no wizard.");
            }

            string grubCfg = GrubConfigBuilder.BuildConfig(
                request.DistroName,
                request.IsoPath,
                includeWindowsChainload: !request.IsUefi,
                enableAutoinstall: request.EnableAutoinstall);

            if (request.IsUefi)
                InstallUefi(request, grubCfg);
            else
                InstallBios(request, grubCfg);
        }

        private void InstallUefi(BootStagingRequest request, string grubCfg)
        {
            // A ESP de SISTEMA, não a do disco alvo: o que está sendo instalado aqui é uma
            // entrada de boot da firmware que ainda vai rodar sob o Windows atual, e a firmware
            // só lê a ESP de onde ela bootou. Procurar no disco alvo assumia que instalar é
            // sempre no disco do Windows — num segundo disco (sem ESP nenhuma) a busca voltava
            // vazia e a instalação morria aqui.
            EfiSystemPartitionLocation? esp = _espLocator.FindSystemEfiSystemPartition();
            if (esp is null)
            {
                throw new InvalidOperationException(
                    "Não foi possível localizar a EFI System Partition de onde esta máquina bootou, " +
                    "necessária para registrar a entrada de boot da instalação.");
            }

            char driveLetter = PickFreeDriveLetter();
            string grubEfiSource = _grubAssets.GetUefiBootloaderPath();
            const string efiPathOnVolume = @"\EFI\linuxhub\grubx64.efi";

            // Montagem, cópia e registro BCD precisam rodar no MESMO script elevado, com o
            // registro BCD ANTES de desmontar a ESP — bcdedit resolve `device partition=X:`
            // consultando a letra de unidade montada; se ela já tiver sido desmontada, falha
            // com "dispositivo não é válido como especificado" (bug encontrado em teste real).
            string script = BuildEspStagingAndBcdScript(
                esp.DiskIndex, esp.PartitionIndex, driveLetter, grubEfiSource, grubCfg,
                description: $"{request.DistroName} ({BootConfigurationService.StagingEntryMarker})",
                efiPathOnVolume: efiPathOnVolume);

            string output = ElevatedPowerShellRunner.Run(script, "instalação do GRUB2 na EFI System Partition e registro da entrada BCD");
            BootConfigurationService.ExtractGuidOrThrow(output);
        }

        private void InstallBios(BootStagingRequest request, string grubCfg)
        {
            string backupPath = Path.Combine(
                Path.GetTempPath(), $"linuxhub_mbr_backup_disk{request.TargetDiskIndex}.bin");
            _mbrBackup.BackupMbr(request.TargetDiskIndex, backupPath);

            string coreImagePath = _grubAssets.GetBiosCoreImagePath();
            EnsurePostMbrGapFitsCoreImage(request.TargetDiskIndex, coreImagePath);

            // Sem fallback para "C:\" (mesma razão que em DiskInventoryService.FindSystemDiskIndex):
            // o grub.cfg gravado na unidade errada não é um erro visível, é um GRUB que sobe no
            // reboot e não acha a própria configuração.
            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)
                ?? throw new InvalidOperationException(
                    "Não foi possível determinar a unidade onde o Windows está instalado para " +
                    $"gravar o grub.cfg (diretório de sistema: '{Environment.SystemDirectory}').");
            ElevatedPowerShellRunner.Run(
                BuildGrubCfgWriteScript(systemDrive, grubCfg), "gravação do grub.cfg na partição do Windows");

            // Ordem importa: o core.img precisa estar no gap ANTES do boot.img ser escrito no
            // MBR, porque assim que o boot.img novo está lá, o disco já está "GRUB-aware" —
            // se faltasse o core.img nesse meio-tempo, um boot acidental encontraria um GRUB
            // incompleto. O backup do MBR (acima) já aconteceu antes de qualquer uma dessas
            // duas escritas, como a spec exige.
            _mbrBackup.WriteCoreImageToGap(request.TargetDiskIndex, coreImagePath);
            _mbrBackup.WriteBootCode(request.TargetDiskIndex, _grubAssets.GetBiosBootSectorPath());
        }

        /// <summary>
        /// O <c>core.img</c> é sempre embutido a partir do LBA 1 (logo após o MBR) — mesmo
        /// comportamento do <c>grub-bios-setup</c> real num disco MBR comum sem partição
        /// <c>bios_grub</c> dedicada (verificado via WSL, ver Assets/Grub/README.md). Aborta
        /// antes de qualquer escrita se o espaço não alocado entre o MBR e a primeira
        /// partição (o gap que o Windows deixa por alinhamento de 1MiB desde o Vista SP1) for
        /// pequeno demais — nunca escreve por cima de uma partição existente.
        /// </summary>
        internal void EnsurePostMbrGapFitsCoreImage(int diskIndex, string coreImagePath)
        {
            byte[] mbr = _mbrBackup.ReadMbr(diskIndex);
            uint gapSectors = MbrPartitionTableReader.GapSectorsAfterMbr(mbr);

            long coreImageSize = new FileInfo(coreImagePath).Length;
            long requiredSectors = (coreImageSize + 511) / 512;

            if (gapSectors < requiredSectors)
            {
                throw new InvalidOperationException(
                    $"Espaço não alocado entre o MBR e a primeira partição do disco {diskIndex} " +
                    $"({gapSectors} setores) é pequeno demais para o core.img do GRUB " +
                    $"({requiredSectors} setores necessários) — abortando antes de qualquer escrita.");
            }
        }

        internal static char PickFreeDriveLetter()
        {
            var used = DriveInfo.GetDrives()
                .Select(d => char.ToUpperInvariant(d.Name[0]))
                .ToHashSet();

            for (char letter = 'Z'; letter >= 'D'; letter--)
            {
                if (!used.Contains(letter))
                    return letter;
            }

            throw new InvalidOperationException(
                "Nenhuma letra de unidade livre para montar a EFI System Partition temporariamente.");
        }

        internal static string BuildEspStagingScript(
            int diskIndex, int partitionIndex, char driveLetter, string grubEfiSourcePath, string grubCfgContent) => $@"
$ErrorActionPreference = 'Stop'
Add-PartitionAccessPath -DiskNumber {diskIndex} -PartitionNumber {partitionIndex} -AccessPath '{driveLetter}:\'
try {{
    New-Item -ItemType Directory -Force -Path '{driveLetter}:\EFI\linuxhub' | Out-Null
    Copy-Item -Path '{grubEfiSourcePath}' -Destination '{driveLetter}:\EFI\linuxhub\grubx64.efi' -Force
    Set-Content -LiteralPath '{driveLetter}:\EFI\linuxhub\grub.cfg' -Value @'
{grubCfgContent}
'@ -NoNewline -Encoding ASCII
}} finally {{
    Remove-PartitionAccessPath -DiskNumber {diskIndex} -PartitionNumber {partitionIndex} -AccessPath '{driveLetter}:\'
}}";

        /// <summary>
        /// Igual a <see cref="BuildEspStagingScript"/>, mas roda os comandos <c>bcdedit</c>
        /// (<see cref="BootConfigurationService.BuildAddFirmwareBootEntryCommands"/>) DENTRO
        /// do mesmo <c>try</c>, antes do <c>finally</c> desmontar a ESP — precisa ser um
        /// script só, não duas execuções elevadas separadas (ver InstallUefi).
        /// </summary>
        internal static string BuildEspStagingAndBcdScript(
            int diskIndex, int partitionIndex, char driveLetter, string grubEfiSourcePath, string grubCfgContent,
            string description, string efiPathOnVolume) => $@"
$ErrorActionPreference = 'Stop'
Add-PartitionAccessPath -DiskNumber {diskIndex} -PartitionNumber {partitionIndex} -AccessPath '{driveLetter}:\'
try {{
    New-Item -ItemType Directory -Force -Path '{driveLetter}:\EFI\linuxhub' | Out-Null
    Copy-Item -Path '{grubEfiSourcePath}' -Destination '{driveLetter}:\EFI\linuxhub\grubx64.efi' -Force
    Set-Content -LiteralPath '{driveLetter}:\EFI\linuxhub\grub.cfg' -Value @'
{grubCfgContent}
'@ -NoNewline -Encoding ASCII

{BootConfigurationService.BuildAddFirmwareBootEntryCommands(description, driveLetter, efiPathOnVolume)}
}} finally {{
    Remove-PartitionAccessPath -DiskNumber {diskIndex} -PartitionNumber {partitionIndex} -AccessPath '{driveLetter}:\'
}}";

        internal static string BuildGrubCfgWriteScript(string systemDrive, string grubCfgContent) => $@"
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path '{systemDrive}boot\grub' | Out-Null
Set-Content -LiteralPath '{systemDrive}boot\grub\grub.cfg' -Value @'
{grubCfgContent}
'@ -NoNewline -Encoding ASCII";
    }
}
