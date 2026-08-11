[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 2147483647)]
    [int]$DiskNumber,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 9223372036854775807)]
    [Int64]$OffsetBytes,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 9223372036854775807)]
    [Int64]$SizeBytes
)

$ErrorActionPreference = 'Stop'

# Ownership must already be proven by the C# caller (DiskOwnershipProof). This script
# only removes the partition whose offset AND size match exactly — never "the last partition".
$matches = @(
    Get-Partition -DiskNumber $DiskNumber -ErrorAction Stop |
        Where-Object {
            [int64]$_.Offset -eq $OffsetBytes -and
            [int64]$_.Size -eq $SizeBytes
        }
)

if ($matches.Count -eq 0) {
    throw "No partition at disk $DiskNumber offset $OffsetBytes size $SizeBytes."
}
if ($matches.Count -gt 1) {
    throw "Ambiguous partition match at disk $DiskNumber offset $OffsetBytes size $SizeBytes."
}

Remove-Partition -DiskNumber $DiskNumber -PartitionNumber $matches[0].PartitionNumber -Confirm:$false
Write-Output "PARTITION_REMOVED=true"
Write-Output ("PARTITION_NUMBER={0}" -f $matches[0].PartitionNumber)
