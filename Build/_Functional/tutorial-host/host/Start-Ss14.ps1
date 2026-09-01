#Requires -Version 5.1
# Starts TutorialServer Content.Server on 127.0.0.1 only (no ASP.NET firewall prompt).
# Public clients reach it via Start-Relay.ps1 (Python on PublicHost:PublicPort).
# SQLite lives at $DataRoot (--data-dir), not under live\bin, so deploys cannot overwrite it.
# Production CVars (hub advertise, live tutorials, no development preset) are set here,
# not in server_config.toml, so a local DebugOpt run stays off the hub.
param(
    [int] $ReadyTimeoutSec = 180
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\HostPaths.ps1"

if (-not (Test-Path -LiteralPath $DotnetExe)) {
    throw "dotnet runtime missing: $DotnetExe"
}
if (-not (Test-Path -LiteralPath $ServerDll)) {
    throw "Missing server dll: $ServerDll (deploy a package first)"
}

& "$PSScriptRoot\Stop-Ss14.ps1"
Ensure-TutorialHostDataDir

New-Item -ItemType Directory -Path $LogsRoot -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outLog = Join-Path $LogsRoot "ss14-$stamp.out.log"
$errLog = Join-Path $LogsRoot "ss14-$stamp.err.log"

# Start-Process ArgumentList is unreliable with spaces; build one quoted command line.
function Q([string]$s) {
    if ($s -match '[\s"]') { return '"' + ($s -replace '"', '\"') + '"' }
    return $s
}

$argList = @(
    (Q $ServerDll),
    '--data-dir', (Q $DataRoot),
    '--cvar', 'log.level=Info',
    # DebugOpt/TOOLS builds load development.toml unless this is false.
    '--cvar', 'config.preset_development=false',
    '--cvar', 'game.lobbyenabled=true',
    '--cvar', 'game.defaultpreset=TutorialServer',
    '--cvar', 'game.map=TutorialLobby',
    '--cvar', 'game.lobbyduration=120',
    '--cvar', (Q 'game.hostname=The Functional Tutorial Server'),
    '--cvar', 'hub.advertise=true',
    '--cvar', 'hub.hub_urls=https://hub.spacestation14.com/',
    '--cvar', 'hub.server_url=ss14://ss14tutorial.mynetgear.com:1212',
    '--cvar', 'hub.tags=lang:en,region:am_n_c',
    '--cvar', 'tutorial.live_tutorials=true',
    '--cvar', 'net.upnp=false',
    '--cvar', "net.port=$GamePort",
    '--cvar', "net.bindto=$BindHost",
    '--cvar', "status.bind=${BindHost}:${GamePort}",
    '--cvar', 'status.connectaddress=',
    '--cvar', 'console.loginlocal=true',
    '--cvar', 'infolinks.discord=https://discord.gg/jkvg7k9w2n',
    '--cvar', 'infolinks.github=https://github.com/FunctionalTutorial/TheFunctionalTutorial'
)
$argString = ($argList -join ' ')

Write-Host "Starting SS14 (localhost only) ${BindHost}:${GamePort}"
Write-Host "Public path via relay: ${PublicHost}:${PublicPort}"
Write-Host "WorkingDirectory=$LiveRoot"
Write-Host "DataDir=$DataRoot"
Write-Host "Logs: $outLog / $errLog"

$proc = Start-Process -FilePath $DotnetExe -ArgumentList $argString `
    -WorkingDirectory $LiveRoot `
    -WindowStyle Hidden `
    -RedirectStandardOutput $outLog `
    -RedirectStandardError $errLog `
    -PassThru

Set-Content -LiteralPath $Ss14PidFile -Value $proc.Id -Encoding ascii
Set-Content -LiteralPath (Join-Path $StateRoot 'ss14.out.log') -Value $outLog -Encoding ascii
Set-Content -LiteralPath (Join-Path $StateRoot 'ss14.err.log') -Value $errLog -Encoding ascii

$deadline = [datetime]::UtcNow.AddSeconds($ReadyTimeoutSec)
$ready = $false
while ([datetime]::UtcNow -lt $deadline) {
    if ($proc.HasExited) {
        throw "SS14 exited early (code $($proc.ExitCode)). See $outLog / $errLog"
    }
    $text = ''
    if (Test-Path $outLog) { $text += Get-Content $outLog -Raw -ErrorAction SilentlyContinue }
    if (Test-Path $errLog) { $text += Get-Content $errLog -Raw -ErrorAction SilentlyContinue }
    if ($text -match '-> Ready') {
        $ready = $true
        break
    }
    if ($text -match 'Unhandled exception|Fatal error|FATL') {
        throw "SS14 crash during boot. See $outLog / $errLog"
    }
    Start-Sleep -Milliseconds 500
}

if (-not $ready) {
    throw "Timed out waiting for SS14 Ready (${ReadyTimeoutSec}s). See $outLog / $errLog"
}

& "$PSScriptRoot\Start-Relay.ps1"

Write-Host "SS14 Ready (PID $($proc.Id)) localhost:${GamePort}; clients use ${PublicHost}:${PublicPort}"
