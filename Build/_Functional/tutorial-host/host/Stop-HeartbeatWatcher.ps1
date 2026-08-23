#Requires -Version 5.1
$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\HostPaths.ps1"

if (Test-Path -LiteralPath $HeartbeatPidFile) {
    $pidText = (Get-Content $HeartbeatPidFile -Raw).Trim()
    if ($pidText -match '^\d+$') {
        $p = Get-Process -Id $pidText -ErrorAction SilentlyContinue
        if ($p -and $p.ProcessName -eq 'powershell') {
            Stop-Process -Id $pidText -Force -ErrorAction SilentlyContinue
            Write-Host "Stopped heartbeat watcher PID $pidText"
        }
    }
    Remove-Item $HeartbeatPidFile -Force -ErrorAction SilentlyContinue
}

Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -match 'Watch-TutorialHostHeartbeat\.ps1' } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Host "Stopped heartbeat watch PID $($_.ProcessId)"
    }
