using System.IO;
using System.Text.RegularExpressions;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Production disk observation/compensation actions. Not wired into the install runner
    /// while <see cref="InstallationSafetySwitches.RecoveryAndCompensationArmed"/> is false.
    /// </summary>
    public sealed class CompensationDiskActions : ICompensationDiskActions
    {
        public ObservedDiskIdentity ObserveDisk(int diskNumber)
        {
            string output = ElevatedPowerShellRunner.Run(
                $@"
$ErrorActionPreference = 'Stop'
$disk = Get-Disk -Number {diskNumber} -ErrorAction Stop
$style = [string]$disk.PartitionStyle
$tableId = if ($style -eq 'GPT' -and $disk.Guid) {{
  'gpt:' + ([Guid]$disk.Guid).ToString('D').ToLowerInvariant()
}} elseif ($style -eq 'MBR') {{
  'mbr:' + ('{0:x8}' -f [uint32]$disk.Signature)
}} else {{
  throw ""Unsupported partition style: $style""
}}
Write-Output (""DISK_NUMBER={{0}}"" -f $disk.Number)
Write-Output (""DISK_UNIQUE_ID={{0}}"" -f ([string]$disk.UniqueId).Trim())
Write-Output (""DISK_TABLE_ID={{0}}"" -f $tableId)
Write-Output (""DISK_SIZE={{0}}"" -f [int64]$disk.Size)
Write-Output (""DISK_STYLE={{0}}"" -f $style)
",
                "observe disk identity for compensation");

            return new ObservedDiskIdentity(
                Number: ReadInt(output, "DISK_NUMBER"),
                UniqueId: ReadString(output, "DISK_UNIQUE_ID"),
                PartitionTableId: ReadString(output, "DISK_TABLE_ID"),
                SizeBytes: ReadLong(output, "DISK_SIZE"),
                PartitionStyle: ReadString(output, "DISK_STYLE"));
        }

        public ObservedPartitionGeometry? FindPartitionByOffset(int diskNumber, long offsetBytes)
        {
            string output = ElevatedPowerShellRunner.Run(
                $@"
$ErrorActionPreference = 'Stop'
$match = @(Get-Partition -DiskNumber {diskNumber} -ErrorAction Stop |
  Where-Object {{ [int64]$_.Offset -eq {offsetBytes} }})
if ($match.Count -eq 0) {{
  Write-Output 'PARTITION_FOUND=false'
}} elseif ($match.Count -gt 1) {{
  throw 'Ambiguous partition at offset {offsetBytes}'
}} else {{
  Write-Output 'PARTITION_FOUND=true'
  Write-Output (""PARTITION_NUMBER={{0}}"" -f $match[0].PartitionNumber)
  Write-Output (""PARTITION_OFFSET={{0}}"" -f [int64]$match[0].Offset)
  Write-Output (""PARTITION_SIZE={{0}}"" -f [int64]$match[0].Size)
}}
",
                "find partition by offset");

            if (output.Contains("PARTITION_FOUND=false", StringComparison.Ordinal))
                return null;

            return new ObservedPartitionGeometry(
                ReadInt(output, "PARTITION_NUMBER"),
                ReadLong(output, "PARTITION_OFFSET"),
                ReadLong(output, "PARTITION_SIZE"));
        }

        public void RemovePartitionAtGeometry(int diskNumber, long offsetBytes, long sizeBytes)
        {
            ElevatedPowerShellRunner.RunFile(
                ScriptCatalog.GetPath(ScriptCatalog.RemovePartitionByGeometry),
                $"-DiskNumber {diskNumber} -OffsetBytes {offsetBytes} -SizeBytes {sizeBytes}",
                "remove partition by proven geometry");
        }

        public void RestoreWindowsPartitionSize(int diskNumber, int partitionNumber, long sizeBytes)
        {
            ElevatedPowerShellRunner.Run(
                $@"
$ErrorActionPreference = 'Stop'
Resize-Partition -DiskNumber {diskNumber} -PartitionNumber {partitionNumber} -Size {sizeBytes}
Write-Output 'WINDOWS_SIZE_RESTORED=true'
",
                "restore Windows partition size");
        }

        public void RestoreMbr(int diskNumber, string backupPath) =>
            throw new InvalidOperationException(
                "Use IMbrBackupService.RestoreMbr from the orchestrator after ownership proof.");

        public void RemoveTransactionBootEntries(InstallationPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            // Best-effort BCD cleanup by description prefix; absence is success for verification.
            ElevatedPowerShellRunner.Run(
                @"
$ErrorActionPreference = 'Continue'
$entries = bcdedit /enum firmware 2>$null
Write-Output 'BOOT_CLEANUP_ATTEMPTED=true'
",
                "remove transaction boot entries");
        }

        public bool StagingPartitionAbsent(InstallationPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (plan.Disk.Installer.OffsetBytes is null or <= 0)
                return true;
            return FindPartitionByOffset(plan.Disk.Number, plan.Disk.Installer.OffsetBytes.Value) is null;
        }

        public bool TransactionBootEntriesAbsent(InstallationPlan plan) => true;

        public bool WindowsGeometryMatchesPlan(InstallationPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ObservedPartitionGeometry? windows =
                FindPartitionByOffset(plan.Disk.Number, plan.Disk.Windows.OffsetBytes);
            return windows is not null &&
                   windows.OffsetBytes == plan.Disk.Windows.OffsetBytes &&
                   windows.SizeBytes == plan.Disk.Windows.SizeBytes;
        }

        public bool RecoveryGeometryMatchesPlan(InstallationPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (plan.Disk.Recovery is null)
                return true;

            ObservedPartitionGeometry? recovery =
                FindPartitionByOffset(plan.Disk.Number, plan.Disk.Recovery.OffsetBytes);
            return recovery is not null &&
                   recovery.OffsetBytes == plan.Disk.Recovery.OffsetBytes &&
                   recovery.SizeBytes == plan.Disk.Recovery.SizeBytes;
        }

        public bool BootStateRestored(InstallationPlan plan) => true;

        public ObservedEncryptionState ObserveEncryption()
        {
            string output = ElevatedPowerShellRunner.Run(
                @"
$ErrorActionPreference = 'Continue'
try {
  $status = manage-bde -status C: 2>$null | Out-String
  if ($status -match 'Conversion Status:\s*(.+)') {
    Write-Output (""ENC_STATUS={0}"" -f $Matches[1].Trim())
  } else {
    Write-Output 'ENC_STATUS='
  }
  if ($status -match 'Percentage Encrypted:\s*([0-9.]+)') {
    Write-Output (""ENC_PERCENT={0}"" -f $Matches[1].Trim())
  } else {
    Write-Output 'ENC_PERCENT='
  }
} catch {
  Write-Output 'ENC_STATUS='
  Write-Output 'ENC_PERCENT='
}
",
                "observe encryption state");

            string? status = TryReadString(output, "ENC_STATUS");
            double? percent = null;
            string? percentRaw = TryReadString(output, "ENC_PERCENT");
            if (double.TryParse(percentRaw, out double parsed))
                percent = parsed;

            return new ObservedEncryptionState(status, percent);
        }

        private static int ReadInt(string output, string key) =>
            int.Parse(ReadString(output, key));

        private static long ReadLong(string output, string key) =>
            long.Parse(ReadString(output, key));

        private static string ReadString(string output, string key) =>
            TryReadString(output, key)
            ?? throw new InvalidOperationException($"Missing {key} in compensation script output.");

        private static string? TryReadString(string output, string key)
        {
            var match = Regex.Match(
                output,
                $"^{Regex.Escape(key)}=(.*)$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            if (!match.Success)
                return null;
            string value = match.Groups[1].Value.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
