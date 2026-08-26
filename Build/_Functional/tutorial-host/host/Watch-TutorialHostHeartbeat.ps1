#Requires -Version 5.1
<#
.SYNOPSIS
  Keep Drive host-status/heartbeat.json + mcp.token current (IP and MCP bearer).

.DESCRIPTION
  Re-reads windows-mcp config.toml auth_key each tick so a regenerated token
  lands in the Drive drop. Does not touch SS14, relay, MCP, or FileShare.
#>
param(
    [int] $PollSeconds = 30
)

$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\HostPaths.ps1"
. "$PSScriptRoot\Publish-TutorialHostHeartbeat.ps1"

New-Item -ItemType Directory -Path $StateRoot, $LogsRoot, (Split-Path -Parent $HeartbeatJson) -Force | Out-Null
Set-Content -LiteralPath $HeartbeatPidFile -Value $PID -Encoding ascii

$log = Join-Path $LogsRoot 'heartbeat.log'
function Write-Hb([string]$msg) {
    $line = "{0:u} {1}" -f [datetime]::UtcNow, $msg
    Add-Content -LiteralPath $log -Value $line -Encoding utf8
}

Write-Hb "Heartbeat watcher starting (PID $PID). Poll=${PollSeconds}s"

while ($true) {
    try {
        Publish-TutorialHostHeartbeat
    }
    catch {
        Write-Hb "Publish FAILED: $($_.Exception.Message)"
    }
    Start-Sleep -Seconds $PollSeconds
}
