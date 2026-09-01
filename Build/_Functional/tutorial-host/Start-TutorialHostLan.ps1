#Requires -Version 5.1
<#
.SYNOPSIS
  Stop supervisor/relay/old SS14, then run TutorialServer bound to the LAN IP.

.DESCRIPTION
  Intended for the physical Tutorial host (192.168.1.4) when you are at the keyboard
  and can Accept the Windows firewall prompt for dotnet.

  Does NOT touch windows-mcp or FileShare.
  Do not run Start-Ss14.ps1 / Start-Supervisor.ps1 while this is up.

  SQLite is --data-dir D:\WizdenHost\data (not live\bin\Content.Server\data).
  WorkingDirectory does not control the database path.

  Production CVars are set here (hub.advertise, tutorial.live_tutorials,
  config.preset_development=false). server_config.toml stays at local/dev defaults.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File D:\WizdenHost\tools\Start-TutorialHostLan.ps1
#>
param(
    [string] $LiveRoot = 'D:\WizdenHost\live',
    [string] $ToolsRoot = 'D:\WizdenHost\tools',
    [string] $DataRoot = 'D:\WizdenHost\data',
    [string] $DotnetExe = 'D:\dotnet\dotnet.exe',
    [string] $BindHost = '192.168.1.4',
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
    Write-Host "No Content.Client.zip - launcher clients will use Magic ACZ from bin/Content.Client"
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

# Avoid Get-NetTCPConnection here - it can hang on this host. Use netstat instead.
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

# loginlocal only promotes 127.0.0.1 clients. LAN bind means you need login_host_user.
# Prefer gitignored server_config.local.toml next to this script, then next to the server dll.
function Get-LoginHostUser([string[]]$CandidatePaths) {
    foreach ($candidate in $CandidatePaths) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        foreach ($line in Get-Content -LiteralPath $candidate) {
            if ($line -match '^\s*login_host_user\s*=\s*"?([^"#]+)"?\s*$') {
                $name = $Matches[1].Trim()
                if ($name.Length -gt 0) {
                    Write-Host "Using console.login_host_user='$name' from $candidate"
                    return $name
                }
            }
        }
    }
    return $null
}

$loginHostUser = Get-LoginHostUser @(
    (Join-Path $PSScriptRoot 'server_config.local.toml'),
    (Join-Path $LiveRoot 'bin\Content.Server\server_config.local.toml'),
    (Join-Path $ToolsRoot 'server_config.local.toml')
)

# Engine default is <dll-dir>/data; keep SQLite outside live/bin so deploys cannot overwrite it.
New-Item -ItemType Directory -Path $DataRoot -Force | Out-Null
$destDb = Join-Path $DataRoot 'preferences.db'
$legacyData = Join-Path $LiveRoot 'bin\Content.Server\data'
$legacyDb = Join-Path $legacyData 'preferences.db'
if (-not (Test-Path -LiteralPath $destDb) -and (Test-Path -LiteralPath $legacyDb)) {
    Write-Step "Seeding $DataRoot from legacy $legacyData (one-time)"
    Get-ChildItem -LiteralPath $legacyData -File -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $DataRoot $_.Name) -Force
    }
}

Write-Step "Starting TutorialServer on ${BindHost}:${Port} (hub.advertise=true)"
Write-Host "WorkingDirectory=$LiveRoot"
Write-Host "DataDir=$DataRoot"
Write-Host 'Click Allow if Windows Firewall prompts for dotnet.'
Write-Host 'Connect: ss14://192.168.1.4:1212  (LAN)  or  ss14://ss14tutorial.mynetgear.com:1212  (hub/public)'
Write-Host 'Ctrl+C stops the server when you are done.'
if (-not $loginHostUser) {
    Write-Host "WARNING: no login_host_user found. Copy server_config.local.toml.example -> server_config.local.toml next to this script." -ForegroundColor Yellow
}
Write-Host ''

$argList = @(
    $serverDll,
    '--data-dir', $DataRoot,
    '--cvar', 'hub.advertise=true',
    '--cvar', 'hub.hub_urls=https://hub.spacestation14.com/',
    '--cvar', 'hub.server_url=ss14://ss14tutorial.mynetgear.com:1212',
    '--cvar', 'hub.tags=lang:en,region:am_n_c',
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
    '--cvar', 'game.soft_max_players=40',
    '--cvar', 'server.rules_file=TutorialServerRules',
    '--cvar', 'rules.enabled=true',
    '--cvar', 'rules.time=5',
    '--cvar', 'rules.validity_days=0',
    '--cvar', 'ooc.enabled=false',
    '--cvar', 'ooc.enable_during_round=true',
    '--cvar', 'looc.enabled=false',
    '--cvar', 'dead_chat.enabled=false',
    '--cvar', 'tutorial.live_tutorials=true',
    '--cvar', 'console.loginlocal=true',
    '--cvar', 'infolinks.discord=https://discord.gg/jkvg7k9w2n',
    '--cvar', 'infolinks.github=https://github.com/FunctionalTutorial/TheFunctionalTutorial',
    '--cvar', 'log.level=Info'
)

if ($loginHostUser) {
    $argList += @('--cvar', "console.login_host_user=$loginHostUser")
}

Set-Location -LiteralPath $LiveRoot
& $DotnetExe @argList
$exit = $LASTEXITCODE
if ($exit -ne 0) {
    throw "Content.Server exited with code $exit"
}
