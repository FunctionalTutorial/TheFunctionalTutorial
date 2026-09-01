#Requires -Version 5.1
<#
.SYNOPSIS
  Keeps Tutorial SS14 + Python relay alive; applies Drive-drop update packages.

.DESCRIPTION
  - Watches D:\WizdenHost\drive-drop\incoming\tutorial-update.ready
  - On marker: Apply-TutorialUpdate.ps1 (stop/extract/start SS14 only; MCP/FileShare/SQLite untouched)
  - Publishes host-status/heartbeat.json each loop so the laptop can refresh MCP
  - Restarts localhost Content.Server and public Python relay if they die
  - Content.Server binds 127.0.0.1 only; relay uses firewall-allowed python.exe
#>
param(
    [int] $PollSeconds = 5,
    [int] $CrashBackoffSeconds = 15
)

$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\HostPaths.ps1"
. "$PSScriptRoot\Publish-TutorialHostHeartbeat.ps1"

New-Item -ItemType Directory -Path $StateRoot, $LogsRoot, $LiveRoot, (Join-Path $DriveDropRoot 'incoming'), (Join-Path $DriveDropRoot 'host-status'), (Join-Path $DriveDropRoot 'logs') -Force | Out-Null
Set-Content -LiteralPath $SupervisorPidFile -Value $PID -Encoding ascii

$supervisorLog = Join-Path $LogsRoot 'supervisor.log'
function Write-Sup([string]$msg) {
    $line = "{0:u} {1}" -f [datetime]::UtcNow, $msg
    Add-Content -LiteralPath $supervisorLog -Value $line -Encoding utf8
    Write-Host $line
}

Write-Sup "Supervisor starting (PID $PID). Poll=${PollSeconds}s"
Write-Sup "Public ${PublicHost}:${PublicPort} via Python relay; SS14 on ${BindHost}:${GamePort}"
Write-Sup "Drive drop incoming: $IncomingReady"
Write-Sup 'Will not manage MCP/FileShare/SQLite data dir.'

$lastRelayAttemptUtc = [datetime]::MinValue
$relayRestartCooldownSec = 120

while ($true) {
    try {
        try {
            Publish-TutorialHostHeartbeat
        }
        catch {
            Write-Sup "Heartbeat FAILED: $($_.Exception.Message)"
        }

        if ((Test-Path -LiteralPath $IncomingReady) -and (Test-Path -LiteralPath $IncomingZip)) {
            Write-Sup 'Update marker detected - applying package'
            try {
                & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\Apply-TutorialUpdate.ps1"
                Write-Sup 'Apply finished OK'
            }
            catch {
                Write-Sup "Apply FAILED: $($_.Exception.Message)"
            }
        }

        $hasServer = Test-Path -LiteralPath $ServerDll
        $disabled = Test-Path -LiteralPath $DisableRestartFlag

        if ($hasServer -and -not $disabled) {
            if (-not (Get-TrackedSs14ProcessId)) {
                Write-Sup 'SS14 not running - restarting'
                try {
                    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\Start-Ss14.ps1"
                    Write-Sup 'SS14 restart OK'
                }
                catch {
                    Write-Sup "SS14 restart FAILED: $($_.Exception.Message)"
                    Start-Sleep -Seconds $CrashBackoffSeconds
                }
            }
            elseif (-not (Get-TrackedRelayProcessId)) {
                $relayAge = ([datetime]::UtcNow - $lastRelayAttemptUtc).TotalSeconds
                if ($relayAge -lt $relayRestartCooldownSec) {
                    # Start-Relay can report OK while the pid file is not tracked; do not hammer.
                }
                else {
                    Write-Sup 'Relay not running - restarting relay only'
                    $lastRelayAttemptUtc = [datetime]::UtcNow
                    try {
                        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\Start-Relay.ps1"
                        Write-Sup 'Relay restart OK'
                    }
                    catch {
                        Write-Sup "Relay restart FAILED: $($_.Exception.Message)"
                        Start-Sleep -Seconds $CrashBackoffSeconds
                    }
                }
            }
        }
    }
    catch {
        Write-Sup "Loop error: $($_.Exception.Message)"
    }

    Start-Sleep -Seconds $PollSeconds
}
