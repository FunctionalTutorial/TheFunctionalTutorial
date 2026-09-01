# Shared paths for Tutorial host tooling. Dot-source from other scripts.
# IMPORTANT: Do NOT use Get-NetTCPConnection / Test-NetConnection in hot paths.

$script:WizdenHostRoot = 'D:\WizdenHost'
$script:LiveRoot = Join-Path $WizdenHostRoot 'live'
$script:StagingRoot = Join-Path $WizdenHostRoot 'staging'
$script:StateRoot = Join-Path $WizdenHostRoot 'state'
$script:LogsRoot = Join-Path $WizdenHostRoot 'logs'
$script:PackagesRoot = Join-Path $WizdenHostRoot 'packages'
$script:ToolsRoot = Join-Path $WizdenHostRoot 'tools'
# Persistent SQLite (saved characters, admin ranks). Must stay outside live/bin
# so Apply-TutorialUpdate robocopy cannot overwrite it. The engine default is
# <dll-dir>/data; start scripts pass --data-dir $DataRoot instead.
$script:DataRoot = Join-Path $WizdenHostRoot 'data'
$script:LegacyDataDir = Join-Path $LiveRoot 'bin\Content.Server\data'
$script:DriveDropRoot = Join-Path $WizdenHostRoot 'drive-drop'
$script:DotnetExe = 'D:\dotnet\dotnet.exe'
$script:PythonExe = 'C:\Users\nadin\AppData\Local\Python\pythoncore-3.14-64\python.exe'
$script:RelayScript = Join-Path $ToolsRoot 'ss14_relay.py'
$script:ServerDll = Join-Path $LiveRoot 'bin\Content.Server\Content.Server.dll'
$script:Ss14PidFile = Join-Path $StateRoot 'ss14.pid'
$script:RelayPidFile = Join-Path $StateRoot 'relay.pid'
$script:SupervisorPidFile = Join-Path $StateRoot 'supervisor.pid'
$script:DisableRestartFlag = Join-Path $StateRoot 'disable-restart.flag'
$script:ApplyLockFile = Join-Path $StateRoot 'apply.lock'
$script:IncomingZip = Join-Path $DriveDropRoot 'incoming\tutorial-update.zip'
$script:IncomingReady = Join-Path $DriveDropRoot 'incoming\tutorial-update.ready'
$script:IncomingSha256 = Join-Path $DriveDropRoot 'incoming\tutorial-update.sha256'
$script:HeartbeatJson = Join-Path $DriveDropRoot 'host-status\heartbeat.json'
$script:McpTokenFile = Join-Path $DriveDropRoot 'host-status\mcp.token'
$script:McpUrlFile = Join-Path $DriveDropRoot 'host-status\mcp.url'
$script:HeartbeatPidFile = Join-Path $StateRoot 'heartbeat.pid'
$script:LastApplyJson = Join-Path $DriveDropRoot 'logs\last-apply.json'

# Public LAN endpoint (Python relay - already firewall-allowed).
# Laptop clients should read the current IP from host-status/heartbeat.json.
$script:PublicHost = '192.168.1.4'
$script:PublicPort = 1212

# Content.Server binds localhost only (avoids ASP.NET firewall prompt).
$script:BindHost = '127.0.0.1'
$script:GamePort = 1213

$script:ProtectedProcessNames = @(
    'windows-mcp',
    'Cursor',
    'WindowsTerminal'
)

function Test-IsProtectedProcess {
    param([System.Diagnostics.Process]$Process)
    if ($null -eq $Process) { return $true }
    if ($ProtectedProcessNames -contains $Process.ProcessName) { return $true }
    try {
        $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($Process.Id)" -ErrorAction SilentlyContinue).CommandLine
    } catch { $cmd = $null }
    if (-not $cmd) { return $false }
    if ($cmd -match 'windows-mcp') { return $true }
    if ($cmd -match 'fileshare_server\.py') { return $true }
    if ($cmd -match 'Watch-TutorialServer\.ps1') { return $true }
    return $false
}

function Stop-TreeByPid([int]$ProcId) {
    if ($ProcId -le 0) { return }
    $p = Get-Process -Id $ProcId -ErrorAction SilentlyContinue
    if ($null -eq $p) { return }
    if (Test-IsProtectedProcess $p) {
        Write-Warning "Refusing to kill protected process PID $ProcId ($($p.ProcessName))"
        return
    }
    & taskkill.exe /PID $ProcId /T /F 2>$null | Out-Null
}

function Get-TrackedProcessId([string]$PidFile) {
    if (-not (Test-Path -LiteralPath $PidFile)) { return $null }
    $pidText = (Get-Content -LiteralPath $PidFile -Raw).Trim()
    if ($pidText -notmatch '^\d+$') { return $null }
    $id = [int]$pidText
    if (Get-Process -Id $id -ErrorAction SilentlyContinue) { return $id }
    return $null
}

function Get-TrackedSs14ProcessId { Get-TrackedProcessId $Ss14PidFile }
function Get-TrackedRelayProcessId { Get-TrackedProcessId $RelayPidFile }

# If DataRoot has no preferences.db yet but the pre-move location does, copy it.
# Call only while SS14 is stopped so WAL files are consistent.
function Ensure-TutorialHostDataDir {
    New-Item -ItemType Directory -Path $DataRoot -Force | Out-Null
    $destDb = Join-Path $DataRoot 'preferences.db'
    $legacyDb = Join-Path $LegacyDataDir 'preferences.db'
    if ((Test-Path -LiteralPath $destDb) -or -not (Test-Path -LiteralPath $legacyDb)) {
        return
    }
    Write-Host "Seeding $DataRoot from legacy $LegacyDataDir (one-time)"
    Get-ChildItem -LiteralPath $LegacyDataDir -File -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $DataRoot $_.Name) -Force
    }
}

# Robocopy extras for bin/Content.Server: never copy host SQLite or local config.
# Do not use /MIR — that would delete extra files on live (including data/).
$script:ContentServerRobocopyExcludes = @(
    '/XD', 'data',
    '/XF', '*.db', '*.db-wal', '*.db-shm', 'server_config.local.toml'
)
