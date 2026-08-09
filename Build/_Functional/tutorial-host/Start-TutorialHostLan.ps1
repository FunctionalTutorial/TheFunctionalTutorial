#Requires -Version 5.1
<#
.SYNOPSIS
  Stop supervisor/relay/old SS14, then run TutorialServer bound to the LAN IP.

.DESCRIPTION
  Intended for the physical Tutorial host (192.168.1.22) when you are at the keyboard
  and can Accept the Windows firewall prompt for dotnet.

  Does NOT touch windows-mcp or FileShare.
  Do not run Start-Ss14.ps1 / Start-Supervisor.ps1 while this is up.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File D:\WizdenHost\tools\Start-TutorialHostLan.ps1
#>
param(
    [string] $LiveRoot = 'D:\WizdenHost\live',
    [string] $ToolsRoot = 'D:\WizdenHost\tools',
    [string] $DotnetExe = 'D:\dotnet\dotnet.exe',
    [string] $BindHost = '192.168.1.22',
    [int] $Port = 1212,
    [int] $ReadyTimeoutSec = 180
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}

$serverDll = Join-Path $LiveRoot 'bin\Content.Server\Content.Server.dll'
$clientDll = Join-Path $LiveRoot 'bin\Content.Client\Content.Client.dll'
$clientZip = Join-Path $LiveRoot 'bin\Content.Server\Content.Client.zip'

if (-not (Test-Path -LiteralPath $DotnetExe)) {
    throw "dotnet not found: $DotnetExe"
}
if (-not (Test-Path -LiteralPath $serverDll)) {
    throw "Missing server dll: $serverDll (deploy a package first)"
}
if (-not (Test-Path -LiteralPath $clientDll)) {
    throw "Missing $clientDll - Magic ACZ needs bin/Content.Client next to the server build."
}

# Stale Hybrid ACZ zip wins over Magic ACZ and ships old Shared net types to launcher clients.
if (Test-Path -LiteralPath $clientZip) {
    $zipTime = (Get-Item -LiteralPath $clientZip).LastWriteTimeUtc
    $dllTime = (Get-Item -LiteralPath $clientDll).LastWriteTimeUtc
    if ($zipTime -lt $dllTime) {
        Write-Step "Removing stale Content.Client.zip ($zipTime UTC) older than Content.Client.dll ($dllTime UTC)"
        Remove-Item -LiteralPath $clientZip -Force
    }
    else {
        Write-Host "Using Hybrid ACZ zip from $zipTime UTC"
    }
}
else {
    Write-Host "No Content.Client.zip — launcher clients will use Magic ACZ from bin/Content.Client"
}

Write-Step 'Stopping supervisor / relay / old SS14 (if any)'
foreach ($script in @('Stop-Supervisor.ps1', 'Stop-Relay.ps1', 'Stop-Ss14.ps1')) {
    $path = Join-Path $ToolsRoot $script
    if (Test-Path -LiteralPath $path) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $path
    }
}

# Clear leftovers the stop scripts sometimes miss (stale relay PID / empty-cmdline dotnet).
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.CommandLine -and (
            $_.CommandLine -match 'Content\.Server\.dll' -or
            $_.CommandLine -match 'ss14_relay\.py'
        )
    } |
    ForEach-Object {
        Write-Host "Killing leftover PID $($_.ProcessId)"
        & taskkill.exe /PID $_.ProcessId /T /F 2>$null | Out-Null
    }

# Drop stale relay pid file so a later Start-Relay does not think it is alive
$relayPidFile = Join-Path (Split-Path $ToolsRoot -Parent) 'state\relay.pid'
if (Test-Path -LiteralPath $relayPidFile) {
    Remove-Item -LiteralPath $relayPidFile -Force -ErrorAction SilentlyContinue
}

Start-Sleep -Seconds 2

# Avoid Get-NetTCPConnection here — it can hang on this host. Use netstat instead.
function Get-ListenPids([int[]]$Ports) {
    $pids = @{}
    foreach ($line in (& netstat.exe -ano)) {
        if ($line -notmatch 'LISTENING') { continue }
        foreach ($p in $Ports) {
            if ($line -match ":$p\s+" -and $line -match '(\d+)\s*$') {
                $pids[[int]$Matches[1]] = $true
            }
        }
    }
    return @($pids.Keys)
}

$listenPids = Get-ListenPids @($Port, 1213)
foreach ($pid in $listenPids) {
    if ($pid -le 0) { continue }
    Write-Host "Killing port holder PID $pid"
    & taskkill.exe /PID $pid /T /F 2>$null | Out-Null
}

Start-Sleep -Seconds 1
$still = Get-ListenPids @($Port, 1213)
if ($still.Count -gt 0) {
    throw ("Port $Port and/or 1213 still in use by PID(s): " + ($still -join ', '))
}

Write-Step "Starting TutorialServer on ${BindHost}:${Port} (hub.advertise=false)"
Write-Host "WorkingDirectory=$LiveRoot"
Write-Host 'Click Allow if Windows Firewall prompts for dotnet.'
Write-Host 'Connect: ss14://192.168.1.22:1212  (LAN)  or  ss14://50.24.192.59:1212  (internet)'
Write-Host 'Ctrl+C stops the server when you are done.'
Write-Host ''

$argList = @(
    $serverDll,
    '--cvar', 'hub.advertise=false',
    '--cvar', 'net.upnp=false',
    '--cvar', "net.port=$Port",
    '--cvar', "net.bindto=$BindHost",
    # Wildcard status Host so LAN + public-IP Direct Connect both work
    '--cvar', "status.bind=*:${Port}",
    '--cvar', 'status.connectaddress=',
    '--cvar', 'config.preset_development=false',
    '--cvar', 'game.lobbyenabled=true',
    '--cvar', 'game.defaultpreset=TutorialServer',
    '--cvar', 'game.map=TutorialLobby',
    '--cvar', 'game.lobbyduration=120',
    '--cvar', 'game.hostname=The Functional Tutorial Server',
    '--cvar', 'console.loginlocal=true',
    '--cvar', 'log.level=Info'
)

Set-Location -LiteralPath $LiveRoot
& $DotnetExe @argList
$exit = $LASTEXITCODE
if ($exit -ne 0) {
    throw "Content.Server exited with code $exit"
}
