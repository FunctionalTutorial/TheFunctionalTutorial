#Requires -Version 5.1
<#
.SYNOPSIS
  End-to-end test: boot TutorialServer, connect a client, enter a private tutorial map.

.DESCRIPTION
  Builds (unless -SkipBuild), starts Content.Server with the TutorialServer preset,
  connects Content.Client to 127.0.0.1, auto-readies, force-starts the round, and
  auto-selects TutorialPassenger so a private tutorial map is loaded.

  Success requires the server log line:
    TUTORIAL_E2E: private_map_loaded

  Always uses 127.0.0.1 (never localhost) to avoid Happy Eyeballs races.

  Exit codes:
    0 = private tutorial map loaded; held; clean shutdown
    1 = build/crash/fail
    2 = timed out waiting for tutorial map load
#>
param(
    [string]$RepoRoot = "",
    [string]$Configuration = "Debug",
    [int]$ServerReadyTimeoutSec = 180,
    [int]$TutorialTimeoutSec = 180,
    [int]$HoldSeconds = 12,
    [string]$Username = "TutorialE2EBot",
    [string]$AutoRole = "TutorialPassenger",
    [int]$Port = 1212,
    [switch]$SkipBuild,
    [switch]$Headless
)

$ErrorActionPreference = "Stop"

if (-not $RepoRoot) {
    # Build/_Functional -> Build -> repo root
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

$serverProj = Join-Path $RepoRoot "Content.Server\Content.Server.csproj"
$clientProj = Join-Path $RepoRoot "Content.Client\Content.Client.csproj"
$serverDll = Join-Path $RepoRoot "bin\Content.Server\Content.Server.dll"
$clientDll = Join-Path $RepoRoot "bin\Content.Client\Content.Client.dll"
$logDir = Join-Path $RepoRoot "Build\_Functional\logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$serverOut = Join-Path $logDir "tutorial-e2e-server-$stamp.out.log"
$serverErr = Join-Path $logDir "tutorial-e2e-server-$stamp.err.log"
$clientOut = Join-Path $logDir "tutorial-e2e-client-$stamp.out.log"
$clientErr = Join-Path $logDir "tutorial-e2e-client-$stamp.err.log"

$script:serverProc = $null
$script:clientProc = $null

function Write-Step([string]$msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Stop-Tree([System.Diagnostics.Process]$proc) {
    if ($null -eq $proc) { return }
    try {
        if (-not $proc.HasExited) {
            & taskkill.exe /PID $proc.Id /T /F 2>$null | Out-Null
        }
    } catch { }
}

function Stop-All {
    Stop-Tree $script:clientProc
    Stop-Tree $script:serverProc
    $script:clientProc = $null
    $script:serverProc = $null
}

function Get-LogText([string[]]$paths) {
    $chunks = foreach ($p in $paths) {
        if (Test-Path -LiteralPath $p) {
            Get-Content -LiteralPath $p -Raw -ErrorAction SilentlyContinue
        }
    }
    return ($chunks -join "`n")
}

function Test-LogMatch([string[]]$paths, [string[]]$patterns) {
    $text = Get-LogText $paths
    if ([string]::IsNullOrEmpty($text)) { return $false }
    foreach ($p in $patterns) {
        if ($text -match $p) { return $true }
    }
    return $false
}

function Test-Crash([string[]]$paths) {
    Test-LogMatch $paths @(
        "Unhandled exception",
        "InvalidOperationException",
        "Fatal error",
        "An attempt was made to transition a task to a final state",
        "Duplicate Subscriptions"
    )
}

try {
    Push-Location $RepoRoot

    if (-not $SkipBuild) {
        Write-Step "Building Content.Server ($Configuration)"
        & dotnet build $serverProj -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "Server build failed (exit $LASTEXITCODE)" }

        Write-Step "Building Content.Client ($Configuration)"
        & dotnet build $clientProj -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "Client build failed (exit $LASTEXITCODE)" }
    }

    if (-not (Test-Path -LiteralPath $serverDll)) { throw "Missing $serverDll - build first" }
    if (-not (Test-Path -LiteralPath $clientDll)) { throw "Missing $clientDll - build first" }

    Write-Step "Starting TutorialServer -> $serverOut"
    $serverArgs = @(
        $serverDll,
        "--cvar", "log.level=Info",
        # Development preset forces Dev map; disable so TutorialLobby / TutorialServer win.
        "--cvar", "config.preset_development=false",
        "--cvar", "game.lobbyenabled=true",
        "--cvar", "game.defaultpreset=TutorialServer",
        "--cvar", "game.map=TutorialLobby",
        "--cvar", "game.lobbyduration=120",
        "--cvar", "tutorial.e2e_force_start=true",
        "--cvar", "tutorial.e2e_auto_role=$AutoRole",
        "--cvar", "game.hostname=The Functional Tutorial Server"
    )
    $script:serverProc = Start-Process -FilePath "dotnet" -ArgumentList $serverArgs `
        -WorkingDirectory $RepoRoot -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $serverOut -RedirectStandardError $serverErr

    Write-Step "Waiting for server Ready (timeout ${ServerReadyTimeoutSec}s)"
    $readyDeadline = [datetime]::UtcNow.AddSeconds($ServerReadyTimeoutSec)
    $serverReady = $false
    $serverLogs = @($serverOut, $serverErr)
    while ([datetime]::UtcNow -lt $readyDeadline) {
        if ($script:serverProc.HasExited) {
            throw "Server exited early (code $($script:serverProc.ExitCode)). See $serverOut / $serverErr"
        }
        if (Test-Crash $serverLogs) {
            throw "Server crash detected. See $serverOut / $serverErr"
        }
        if (Test-LogMatch $serverLogs @("-> Ready")) {
            $serverReady = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $serverReady) {
        throw "Timed out waiting for server Ready. See $serverOut / $serverErr"
    }
    Write-Host "Server is Ready."
    Start-Sleep -Seconds 2

    Write-Step "Starting client (connect 127.0.0.1:$Port) -> $clientOut"
    $clientArgs = @(
        $clientDll,
        "--connect",
        "--connect-address", "127.0.0.1:$Port",
        "--username", $Username,
        "--cvar", "tutorial.e2e_auto_ready=true",
        "--loglevel", "net=Debug,info"
    )
    if ($Headless) {
        $clientArgs += "--headless"
    }
    $script:clientProc = Start-Process -FilePath "dotnet" -ArgumentList $clientArgs `
        -WorkingDirectory $RepoRoot -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $clientOut -RedirectStandardError $clientErr

    Write-Step "Waiting for private tutorial map load (timeout ${TutorialTimeoutSec}s)"
    $tutorialDeadline = [datetime]::UtcNow.AddSeconds($TutorialTimeoutSec)
    $tutorialLoaded = $false
    $clientLogs = @($clientOut, $clientErr)
    $connected = $false

    while ([datetime]::UtcNow -lt $tutorialDeadline) {
        if ($script:serverProc.HasExited) {
            throw "Server died (code $($script:serverProc.ExitCode)). See $serverOut"
        }
        if ($script:clientProc.HasExited) {
            throw "Client exited (code $($script:clientProc.ExitCode)). See $clientOut / $clientErr"
        }
        if (Test-Crash $serverLogs) { throw "Server crash. See $serverOut / $serverErr" }
        if (Test-Crash $clientLogs) { throw "Client crash. See $clientOut / $clientErr" }

        if (-not $connected) {
            $clientOk = Test-LogMatch $clientLogs @(
                "Client completed serializer handshake",
                "Client completed transfer handshake",
                "Handshake completed, connection established"
            )
            $serverOk = Test-LogMatch $serverLogs @("Approved .+ into the server")
            if ($clientOk -or $serverOk) {
                $connected = $true
                Write-Host "Client connected."
            }
        }

        if (Test-LogMatch $serverLogs @("TUTORIAL_E2E: private_map_loaded")) {
            $tutorialLoaded = $true
            break
        }

        Start-Sleep -Milliseconds 400
    }

    if (-not $connected) {
        Write-Host "FAIL: never connected. Logs under $logDir" -ForegroundColor Red
        Stop-All
        exit 2
    }

    if (-not $tutorialLoaded) {
        Write-Host "FAIL: timed out waiting for TUTORIAL_E2E: private_map_loaded. Logs under $logDir" -ForegroundColor Red
        Write-Host "---- server log tail ----" -ForegroundColor Yellow
        Get-Content -LiteralPath $serverOut -Tail 40 -ErrorAction SilentlyContinue
        Stop-All
        exit 2
    }

    Write-Host "PASS: private tutorial map loaded for role $AutoRole. Holding ${HoldSeconds}s..." -ForegroundColor Green
    $holdDeadline = [datetime]::UtcNow.AddSeconds($HoldSeconds)
    while ([datetime]::UtcNow -lt $holdDeadline) {
        if ($script:serverProc.HasExited) {
            throw "Server crashed after tutorial load (code $($script:serverProc.ExitCode)). See $serverOut"
        }
        if ($script:clientProc.HasExited) {
            throw "Client crashed after tutorial load (code $($script:clientProc.ExitCode)). See $clientOut"
        }
        if (Test-Crash $serverLogs) { throw "Server crash after tutorial load. See $serverOut / $serverErr" }
        if (Test-Crash $clientLogs) { throw "Client crash after tutorial load. See $clientOut / $clientErr" }
        Start-Sleep -Milliseconds 500
    }

    Write-Step "Shutting down server + client"
    Stop-All
    Write-Host "TUTORIAL E2E OK" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    Stop-All
    exit 1
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    Stop-All
}
