using System.Text.RegularExpressions;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Implementa <see cref="IMbrBackupService"/> via script PowerShell elevado (mesmo
    /// motivo de <see cref="ElevatedPowerShellRunner"/>: acesso bruto a
    /// <c>\\.\PhysicalDriveN</c> exige processo elevado, e não dá pra elevar só a leitura/
    /// escrita de um FileStream já aberto num processo não-elevado).
    /// </summary>
    public sealed partial class MbrBackupService : IMbrBackupService
    {
        internal const int SectorSize = 512;
        internal const int BootCodeSize = 440;

        private readonly IInstallationPlanMutationGuard _mutationGuard;

        public MbrBackupService(IInstallationPlanMutationGuard mutationGuard)
        {
            _mutationGuard = mutationGuard ?? throw new ArgumentNullException(nameof(mutationGuard));
        }

        public string BackupMbr(int diskIndex, string backupPath)
        {
            _mutationGuard.EnsurePublishedForDisk(diskIndex);
            ElevatedPowerShellRunner.Run(BuildBackupScript(diskIndex, backupPath), "backup do MBR");
            return backupPath;
        }

        public void WriteBootCode(int diskIndex, string bootCodeFilePath)
        {
            _mutationGuard.EnsurePublishedForDisk(diskIndex);
            ElevatedPowerShellRunner.Run(
                BuildWriteBootCodeScript(diskIndex, bootCodeFilePath),
                "escrita do código de boot no MBR");
        }

        public void RestoreMbr(int diskIndex, string backupPath)
        {
            _mutationGuard.EnsurePublishedForDisk(diskIndex);
            // Versioned Scripts/ payload (D11 / task 5.1) — disk index and path are arguments,
            // never interpolated into a generated script body.
            ElevatedPowerShellRunner.RunFile(
                ScriptCatalog.GetPath(ScriptCatalog.RestoreMbr),
                $"-DiskNumber {diskIndex} -BackupPath \"{EscapeForPowerShellArgument(backupPath)}\"",
                "restauração do MBR");
        }

        internal static string EscapeForPowerShellArgument(string value) =>
            value.Replace("\"", "`\"", StringComparison.Ordinal);

        public byte[] ReadMbr(int diskIndex)
        {
            _mutationGuard.EnsurePublishedForDisk(diskIndex);
            string output = ElevatedPowerShellRunner.Run(BuildReadMbrScript(diskIndex), "leitura do MBR");

            var match = MbrBase64Regex().Match(output);
            if (!match.Success)
                throw new InvalidOperationException($"Não foi possível ler o MBR do disco {diskIndex}. Saída: {output}");

            byte[] mbr = Convert.FromBase64String(match.Groups[1].Value);
            if (mbr.Length != SectorSize)
                throw new InvalidOperationException($"MBR lido do disco {diskIndex} tem {mbr.Length} bytes, esperado {SectorSize}.");

            return mbr;
        }

        public void WriteCoreImageToGap(int diskIndex, string coreImageFilePath)
        {
            _mutationGuard.EnsurePublishedForDisk(diskIndex);
            ElevatedPowerShellRunner.Run(
                BuildWriteCoreImageScript(diskIndex, coreImageFilePath),
                "escrita do core.img no gap pós-MBR");
        }
        [GeneratedRegex(@"MBRBASE64:([A-Za-z0-9+/=]+)")]
        private static partial Regex MbrBase64Regex();

        internal static string BuildBackupScript(int diskIndex, string backupPath) => $@"
$ErrorActionPreference = 'Stop'
$stream = [System.IO.File]::Open('\\.\PhysicalDrive{diskIndex}', 'Open', 'Read', 'ReadWrite')
try {{
    $buffer = New-Object byte[] {SectorSize}
    $read = $stream.Read($buffer, 0, {SectorSize})
    if ($read -ne {SectorSize}) {{ throw ""Leitura incompleta do MBR: $read de {SectorSize} bytes"" }}
    [System.IO.File]::WriteAllBytes('{backupPath}', $buffer)
}} finally {{
    $stream.Close()
}}";

        internal static string BuildWriteBootCodeScript(int diskIndex, string bootCodeFilePath) => $@"
$ErrorActionPreference = 'Stop'
$bootCode = [System.IO.File]::ReadAllBytes('{bootCodeFilePath}')
if ($bootCode.Length -ne {BootCodeSize}) {{ throw ""Código de boot precisa ter {BootCodeSize} bytes, tem $($bootCode.Length)"" }}
$stream = [System.IO.File]::Open('\\.\PhysicalDrive{diskIndex}', 'Open', 'ReadWrite', 'ReadWrite')
try {{
    $mbr = New-Object byte[] {SectorSize}
    $read = $stream.Read($mbr, 0, {SectorSize})
    if ($read -ne {SectorSize}) {{ throw ""Leitura incompleta do MBR: $read de {SectorSize} bytes"" }}
    [Array]::Copy($bootCode, $mbr, {BootCodeSize})
    $stream.Position = 0
    $stream.Write($mbr, 0, {SectorSize})
}} finally {{
    $stream.Close()
}}";

        internal static string BuildReadMbrScript(int diskIndex) => $@"
$ErrorActionPreference = 'Stop'
$stream = [System.IO.File]::Open('\\.\PhysicalDrive{diskIndex}', 'Open', 'Read', 'ReadWrite')
try {{
    $buffer = New-Object byte[] {SectorSize}
    $read = $stream.Read($buffer, 0, {SectorSize})
    if ($read -ne {SectorSize}) {{ throw ""Leitura incompleta do MBR: $read de {SectorSize} bytes"" }}
    Write-Output ""MBRBASE64:$([Convert]::ToBase64String($buffer))""
}} finally {{
    $stream.Close()
}}";

        internal static string BuildWriteCoreImageScript(int diskIndex, string coreImageFilePath) => $@"
$ErrorActionPreference = 'Stop'
$coreImage = [System.IO.File]::ReadAllBytes('{coreImageFilePath}')
$stream = [System.IO.File]::Open('\\.\PhysicalDrive{diskIndex}', 'Open', 'ReadWrite', 'ReadWrite')
try {{
    $stream.Position = {SectorSize}
    $stream.Write($coreImage, 0, $coreImage.Length)
}} finally {{
    $stream.Close()
}}";
    }
}
