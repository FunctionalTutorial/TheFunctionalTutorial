#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\HostPaths.ps1"

New-Item -ItemType Directory -Path $StateRoot, $LogsRoot -Force | Out-Null

if (Test-Path -LiteralPath $HeartbeatPidFile) {
    $old = (Get-Content $HeartbeatPidFile -Raw).Trim()
    if ($old -match '^\d+$' -and (Get-Process -Id $old -ErrorAction SilentlyContinue)) {
        Write-Host "Heartbeat watcher already running (PID $old)"
        exit 0
    }
}

$existing = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -match 'Watch-TutorialHostHeartbeat\.ps1' }
if ($existing) {
    Write-Host "Heartbeat watcher already running (PID $($existing.ProcessId))"
    Set-Content $HeartbeatPidFile -Value $existing.ProcessId -Encoding ascii
    exit 0
}

$outLog = Join-Path $LogsRoot 'heartbeat.launch.out.log'
$errLog = Join-Path $LogsRoot 'heartbeat.launch.err.log'

$proc = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
    '-NoProfile',
    '-File', "$PSScriptRoot\Watch-TutorialHostHeartbeat.ps1"
) -WorkingDirectory $ToolsRoot -WindowStyle Hidden -RedirectStandardOutput $outLog -RedirectStandardError $errLog -PassThru

Start-Sleep -Seconds 1
if ($proc.HasExited) {
    throw "Heartbeat watcher failed to start. See $outLog / $errLog"
}

Set-Content -LiteralPath $HeartbeatPidFile -Value $proc.Id -Encoding ascii
Write-Host "Heartbeat watcher started PID=$($proc.Id)"
