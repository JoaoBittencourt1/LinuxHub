[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TaskName,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$AgentPath,

    [Parameter(Mandatory = $false)]
    [string]$StatePath = ""
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $AgentPath -PathType Leaf)) {
    throw "Recovery agent script is missing: $AgentPath"
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue

$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries

$powerShell = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$argument = if ([string]::IsNullOrWhiteSpace($StatePath)) {
    '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}"' -f $AgentPath
} else {
    '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" -StatePath "{1}"' -f `
        $AgentPath, $StatePath
}

$action = New-ScheduledTaskAction -Execute $powerShell -Argument $argument
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal `
    -UserId 'SYSTEM' `
    -LogonType ServiceAccount `
    -RunLevel Highest

try {
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Force | Out-Null
}
catch {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    throw
}

$confirmed = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
if ($null -eq $confirmed) {
    throw "Recovery task was not confirmed after registration: $TaskName"
}

Write-Output 'RECOVERY_TASK_REGISTERED=true'
Write-Output ("TASK_NAME={0}" -f $TaskName)
