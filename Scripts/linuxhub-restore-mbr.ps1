[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 2147483647)]
    [int]$DiskNumber,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BackupPath
)

$ErrorActionPreference = 'Stop'

$mbr = [System.IO.File]::ReadAllBytes($BackupPath)
if ($mbr.Length -ne 512) {
    throw "Invalid MBR backup: $($mbr.Length) bytes, expected 512"
}

$stream = [System.IO.File]::Open("\\.\PhysicalDrive$DiskNumber", 'Open', 'ReadWrite', 'ReadWrite')
try {
    $stream.Position = 0
    $stream.Write($mbr, 0, 512)
}
finally {
    $stream.Close()
}

Write-Output "MBR_RESTORED=true"
