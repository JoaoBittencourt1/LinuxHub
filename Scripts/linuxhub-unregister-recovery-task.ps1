[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TaskName
)

$ErrorActionPreference = 'Stop'

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
$stillThere = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -ne $stillThere) {
    throw "Recovery task still exists after unregister: $TaskName"
}

Write-Output 'RECOVERY_TASK_UNREGISTERED=true'
Write-Output ("TASK_NAME={0}" -f $TaskName)
