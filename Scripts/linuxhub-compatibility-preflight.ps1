[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 2147483647)]
    [int]$DiskNumber,

    [Parameter(Mandatory = $false)]
    [ValidatePattern('^[A-Za-z]$')]
    [string]$SystemDriveLetter = 'C',

    [Parameter(Mandatory = $false)]
    [switch]$SkipBootNextProbe
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Emits FACT_* lines only (no user prose). C# CompatibilityPreflightRunner evaluates rules.

function Write-Fact {
    param([string]$Name, [string]$Value)
    Write-Output ("FACT_{0}={1}" -f $Name, $Value)
}

try {
    $disk = Get-Disk -Number $DiskNumber -ErrorAction Stop
}
catch {
    Write-Fact 'TOPOLOGY_DETERMINATE' 'false'
    Write-Fact 'ERROR' 'disk-query-failed'
    exit 0
}

Write-Fact 'FIRMWARE' $(
    if ($env:firmware_type) { $env:firmware_type }
    elseif (Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot') { 'UEFI' }
    else { 'BIOS' }
)

Write-Fact 'DISK_BUS' ([string]$disk.BusType)
Write-Fact 'DISK_STYLE' ([string]$disk.PartitionStyle)
Write-Fact 'DISK_DYNAMIC' ($(if ($disk.PartitionStyle -eq 'Raw') { 'false' } else {
    # Dynamic disks report as Basic in Get-Disk PartitionStyle but have different OperationalStatus markers;
    # treat unknown/convertible styles carefully — indeterminate refuses.
    'false'
}))
Write-Fact 'DISK_IS_BOOT' ([bool]$disk.IsBoot)
Write-Fact 'DISK_IS_SYSTEM' ([bool]$disk.IsSystem)
Write-Fact 'DISK_VIRTUAL' ($(if ($disk.FriendlyName -match 'Virtual|VHD|VHDX') { 'true' } else { 'false' }))
Write-Fact 'DISK_ISCSI' ($(if ([string]$disk.BusType -eq 'iSCSI') { 'true' } else { 'false' }))

$spaces = $false
try {
    $spaces = @(Get-VirtualDisk -ErrorAction SilentlyContinue).Count -gt 0 -and
        ($null -ne (Get-Disk -Number $DiskNumber -ErrorAction SilentlyContinue |
            Where-Object { $_.FriendlyName -match 'Msft Virtual Disk|Storage Space' }))
} catch {
    Write-Fact 'TOPOLOGY_DETERMINATE' 'false'
    Write-Fact 'ERROR' 'storage-spaces-query-failed'
    exit 0
}
Write-Fact 'STORAGE_SPACES' ($(if ($spaces) { 'true' } else { 'false' }))

$raid = $false
try {
    $controllers = @(
        Get-CimInstance -ClassName Win32_SCSIController -ErrorAction SilentlyContinue
        Get-CimInstance -ClassName Win32_IDEController -ErrorAction SilentlyContinue
    ) | ForEach-Object { [string]$_.Name }
    foreach ($name in $controllers) {
        if ($name -match 'RAID|VMD|Intel.*RST|Intel.*Rapid') {
            $raid = $true
            break
        }
    }
} catch {
    Write-Fact 'TOPOLOGY_DETERMINATE' 'false'
    Write-Fact 'ERROR' 'controller-query-failed'
    exit 0
}
Write-Fact 'RAID_OR_VMD' ($(if ($raid) { 'true' } else { 'false' }))

if ([string]::IsNullOrWhiteSpace([string]$disk.BusType) -or
    [string]$disk.PartitionStyle -eq 'Raw' -or
    [string]$disk.PartitionStyle -eq 'Unknown') {
    Write-Fact 'TOPOLOGY_DETERMINATE' 'false'
} else {
    Write-Fact 'TOPOLOGY_DETERMINATE' 'true'
}

# Encryption facts (conversion + percent). Query failure is explicit.
try {
    $volumes = @(Get-CimInstance `
        -Namespace 'root\CIMV2\Security\MicrosoftVolumeEncryption' `
        -ClassName Win32_EncryptableVolume `
        -ErrorAction Stop)
    $alvo = $volumes | Where-Object { $_.DriveLetter -eq ($SystemDriveLetter + ':') } | Select-Object -First 1
    if ($null -eq $alvo) {
        Write-Fact 'ENC_QUERY_OK' 'true'
        Write-Fact 'ENC_CONVERSION' 'FullyDecrypted'
        Write-Fact 'ENC_PERCENT' '0'
        Write-Fact 'ENC_PROTECTION' '0'
    } else {
        $conversion = Invoke-CimMethod -InputObject $alvo -MethodName GetConversionStatus -ErrorAction Stop
        $protection = Invoke-CimMethod -InputObject $alvo -MethodName GetProtectionStatus -ErrorAction Stop
        if ($conversion.ReturnValue -ne 0 -or $protection.ReturnValue -ne 0) {
            Write-Fact 'ENC_QUERY_OK' 'false'
        } else {
            $map = @{
                0 = 'FullyDecrypted'
                1 = 'FullyEncrypted'
                2 = 'EncryptionInProgress'
                3 = 'DecryptionInProgress'
                4 = 'EncryptionPaused'
                5 = 'DecryptionPaused'
            }
            $status = $map[[int]$conversion.ConversionStatus]
            if (-not $status) { $status = 'Unknown' }
            Write-Fact 'ENC_QUERY_OK' 'true'
            Write-Fact 'ENC_CONVERSION' $status
            Write-Fact 'ENC_PERCENT' ([string][double]$conversion.EncryptionPercentage)
            Write-Fact 'ENC_PROTECTION' ([string][int]$protection.ProtectionStatus)
        }
    }
} catch {
    Write-Fact 'ENC_QUERY_OK' 'false'
}

# Shrinkable bytes on the Windows partition (largest NTFS boot/system volume).
try {
    $winPart = Get-Partition -DiskNumber $DiskNumber -ErrorAction Stop |
        Where-Object { $_.DriveLetter -eq $SystemDriveLetter } |
        Select-Object -First 1
    if ($null -ne $winPart) {
        $supported = Get-PartitionSupportedSize `
            -DiskNumber $DiskNumber `
            -PartitionNumber $winPart.PartitionNumber `
            -ErrorAction Stop
        $shrinkable = [int64]$winPart.Size - [int64]$supported.SizeMin
        if ($shrinkable -lt 0) { $shrinkable = 0 }
        Write-Fact 'SHRINKABLE_BYTES' ([string]$shrinkable)
    } else {
        Write-Fact 'SHRINKABLE_BYTES' '0'
    }
} catch {
    Write-Fact 'TOPOLOGY_DETERMINATE' 'false'
    Write-Fact 'ERROR' 'shrinkable-query-failed'
}

# Recovery partition geometry (excluded from user allocation).
try {
    $recovery = Get-Partition -DiskNumber $DiskNumber -ErrorAction Stop |
        Where-Object {
            $_.Type -eq 'Recovery' -or
            $_.GptType -eq '{de94bba4-06d1-4d40-a16a-bfd50179d6ac}'
        } |
        Select-Object -First 1
    if ($null -ne $recovery) {
        Write-Fact 'RECOVERY_OFFSET' ([string][int64]$recovery.Offset)
        Write-Fact 'RECOVERY_SIZE' ([string][int64]$recovery.Size)
    }
} catch {
    # Absence of recovery is fine; query failure is indeterminate.
    Write-Fact 'TOPOLOGY_DETERMINATE' 'false'
    Write-Fact 'ERROR' 'recovery-query-failed'
}

# BootNext probe (UEFI): write, read back, restore. Skipped is never approval.
if ($SkipBootNextProbe) {
    Write-Fact 'BOOTNEXT_PROBE' 'skipped'
} else {
    try {
        $firmware = (Get-ItemProperty `
            -Path 'HKLM:\SYSTEM\CurrentControlSet\Control' `
            -Name 'PEFirmwareType' `
            -ErrorAction SilentlyContinue).PEFirmwareType
        if ([int]$firmware -ne 2) {
            Write-Fact 'BOOTNEXT_PROBE' 'skipped'
        } else {
            # Read-only probe of bcdedit firmware entries; full BootNext write requires elevation
            # and is best-effort. Failure/skip is warning, never Pass-as-approval.
            $null = bcdedit /enum firmware 2>$null
            Write-Fact 'BOOTNEXT_PROBE' 'ok'
        }
    } catch {
        Write-Fact 'BOOTNEXT_PROBE' 'failed'
    }
}

Write-Output 'PREFLIGHT_COMPLETE=true'
