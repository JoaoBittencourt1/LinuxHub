[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$StatePath = "",

    [Parameter(Mandatory = $false)]
    [string]$PlanPath = ""
)

$ErrorActionPreference = 'Stop'

# LinuxHub recovery agent (D5). Runs as SYSTEM AtStartup when a transaction is incomplete.
# Destructive compensation is intentionally unreachable until
# InstallationSafetySwitches.RecoveryAndCompensationArmed is flipped after phase-8 VM proof.
#
# While disarmed, the agent only records that it would have run and exits without mutating disk.

$logRoot = Join-Path $env:ProgramData 'LinuxHub\Logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$logPath = Join-Path $logRoot 'recovery-agent.log'

function Write-AgentLog {
    param([string]$Message)
    $line = '{0:o} {1}' -f [DateTimeOffset]::UtcNow, $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

Write-AgentLog 'linuxhub-recovery-agent starting'

$armedMarker = Join-Path $env:ProgramData 'LinuxHub\recovery-armed.flag'
if (-not (Test-Path -LiteralPath $armedMarker -PathType Leaf)) {
    Write-AgentLog 'DISARMED: recovery-armed.flag absent; refusing compensation (constitution §7.1).'
    Write-Output 'RECOVERY_DISARMED=true'
    exit 0
}

Write-AgentLog 'ARMED marker present — compensation entry point not yet implemented in this agent build.'
Write-Output 'RECOVERY_ARMED_BUT_NOT_IMPLEMENTED=true'
exit 1
