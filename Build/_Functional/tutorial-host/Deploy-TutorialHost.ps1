#Requires -Version 5.1
<#
.SYNOPSIS
  Build Content.Server (+ Content.Client when present), package with Resources, drop onto Google Drive.

.DESCRIPTION
  Local-only deploy helper. Does not touch the remote MCP (port 8000) or FileShare (port 8765).

  Package lands in the mirrored Drive folder:
    incoming/tutorial-update.zip
    incoming/tutorial-update.sha256
    incoming/tutorial-update.ready

  Drop root comes from -DropRoot or %USERPROFILE%\.wizden-host-drive.path.
  The host supervisor (Watch-TutorialServer.ps1) applies the package:
  stop SS14 -> extract -> start SS14 (infra stays up).

  Defaults to Release so the live host does not ship DEBUG asserts
  (DebugTools.Assert / DebugAssertException can crash the process).
  Release builds pass /p:TreatWarningsAsErrors=false so analyzer noise
  does not block packaging; use -Configuration DebugOpt for local assert checks.

.EXAMPLE
  .\Deploy-TutorialHost.ps1

.EXAMPLE
  .\Deploy-TutorialHost.ps1 -SkipBuild

.EXAMPLE
  .\Deploy-TutorialHost.ps1 -Configuration DebugOpt
#>
param(
    [string] $RepoRoot = "",
    [ValidateSet("Debug", "DebugOpt", "Release")]
    [string] $Configuration = "Release",
    [switch] $SkipBuild,
    [string] $DropRoot = "",
    [switch] $ResourcesOnly,
    [switch] $BinOnly
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "Resolve-TutorialHostDropRoot.ps1")

if (-not $RepoRoot) {
    # tutorial-host/ -> _Functional/ -> Build/ -> repo root
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
}

function Write-Step([string]$msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}

$drop = Resolve-TutorialHostDropRoot -DropRoot $DropRoot
$incomingDir = Join-Path $drop "incoming"

$serverProj = Join-Path $RepoRoot "Content.Server\Content.Server.csproj"
$clientProj = Join-Path $RepoRoot "Content.Client\Content.Client.csproj"
$serverDll = Join-Path $RepoRoot "bin\Content.Server\Content.Server.dll"
$clientDll = Join-Path $RepoRoot "bin\Content.Client\Content.Client.dll"
$clientBinDir = Join-Path $RepoRoot "bin\Content.Client"
$resourcesDir = Join-Path $RepoRoot "Resources"
$workRoot = Join-Path $env:TEMP ("wizden-tutorial-host-" + [guid]::NewGuid().ToString("n"))
$stageRoot = Join-Path $workRoot "stage"
$zipPath = Join-Path $workRoot "tutorial-update.zip"

try {
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

    if (-not $SkipBuild -and -not $ResourcesOnly) {
        # Release treats warnings as errors in Content.props; clear that for host packaging.
        $buildArgs = @("--nologo")
        if ($Configuration -eq "Release") {
            $buildArgs += "/p:TreatWarningsAsErrors=false"
        }

        Write-Step "Building Content.Server ($Configuration)"
        & dotnet build $serverProj -c $Configuration @buildArgs
        if ($LASTEXITCODE -ne 0) { throw "dotnet build Content.Server failed (exit $LASTEXITCODE)" }

        Write-Step "Building Content.Client ($Configuration)"
        & dotnet build $clientProj -c $Configuration @buildArgs
        if ($LASTEXITCODE -ne 0) { throw "dotnet build Content.Client failed (exit $LASTEXITCODE)" }
    }

    if (-not (Test-Path -LiteralPath $serverDll) -and -not $ResourcesOnly) {
        throw "Missing $serverDll - build first or omit -ResourcesOnly"
    }
    if (-not (Test-Path -LiteralPath $resourcesDir)) {
        throw "Missing Resources directory: $resourcesDir"
    }

    if (-not $ResourcesOnly) {
        Write-Step "Staging bin/Content.Server"
        $destBin = Join-Path $stageRoot "bin\Content.Server"
        New-Item -ItemType Directory -Path $destBin -Force | Out-Null
        & robocopy.exe (Join-Path $RepoRoot "bin\Content.Server") $destBin /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy bin/Content.Server failed (exit $LASTEXITCODE)" }

        # Hybrid ACZ prefers Content.Client.zip over Magic ACZ. A stale zip (common when only
        # DLLs are rebuilt) makes launcher clients deserialize against old Shared net types and
        # disconnect with "Failed to deserialize packet" on tutorial role select.
        $stagedClientZip = Join-Path $destBin "Content.Client.zip"
        if (Test-Path -LiteralPath $stagedClientZip) {
            Write-Step "Removing staged Content.Client.zip so host uses Magic ACZ from bin/Content.Client"
            Remove-Item -LiteralPath $stagedClientZip -Force
        }

        # Magic ACZ: live/bin/Content.Client (content root = live/, assemblies under bin/Content.Client).
        if (Test-Path -LiteralPath $clientDll) {
            Write-Step "Staging bin/Content.Client"
            $destClient = Join-Path $stageRoot "bin\Content.Client"
            New-Item -ItemType Directory -Path $destClient -Force | Out-Null
            & robocopy.exe $clientBinDir $destClient /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy bin/Content.Client failed (exit $LASTEXITCODE)" }
        }
        else {
            Write-Warning "Missing $clientDll - package will not include bin/Content.Client"
        }
    }

    if (-not $BinOnly) {
        Write-Step "Staging Resources"
        $destRes = Join-Path $stageRoot "Resources"
        New-Item -ItemType Directory -Path $destRes -Force | Out-Null
        & robocopy.exe $resourcesDir $destRes /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy Resources failed (exit $LASTEXITCODE)" }

        # Non-FULL_RELEASE server mounts engine resources from RobustToolbox/Resources.
        $rtRes = Join-Path $RepoRoot "RobustToolbox\Resources"
        if (Test-Path -LiteralPath $rtRes) {
            Write-Step "Staging RobustToolbox/Resources"
            $destRt = Join-Path $stageRoot "RobustToolbox\Resources"
            New-Item -ItemType Directory -Path $destRt -Force | Out-Null
            & robocopy.exe $rtRes $destRt /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy RobustToolbox/Resources failed (exit $LASTEXITCODE)" }
        }
    }

    $manifest = [ordered]@{
        CreatedUtc     = [datetime]::UtcNow.ToString("o")
        Configuration  = $Configuration
        Machine        = $env:COMPUTERNAME
        RepoRoot       = $RepoRoot
        IncludesBin    = (-not $ResourcesOnly)
        IncludesResources = (-not $BinOnly)
        ServerDllExists = (Test-Path -LiteralPath (Join-Path $stageRoot "bin\Content.Server\Content.Server.dll"))
        ClientDllExists = (Test-Path -LiteralPath (Join-Path $stageRoot "bin\Content.Client\Content.Client.dll"))
        # Intentionally omitted: stale Hybrid ACZ zips break launcher clients. Host uses Magic ACZ.
        ClientZipExists = $false
        UsesMagicAcz = (Test-Path -LiteralPath (Join-Path $stageRoot "bin\Content.Client\Content.Client.dll"))
    }
    ($manifest | ConvertTo-Json) | Set-Content -LiteralPath (Join-Path $stageRoot "deploy-manifest.json") -Encoding UTF8

    Write-Step "Creating zip"
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Push-Location $stageRoot
    try {
        & tar.exe -a -c -f $zipPath *
        if ($LASTEXITCODE -ne 0) { throw "tar zip failed (exit $LASTEXITCODE)" }
    }
    finally {
        Pop-Location
    }

    $zipItem = Get-Item -LiteralPath $zipPath
    $mb = [math]::Round($zipItem.Length / 1MB, 1)
    Write-Step "Copying package to Drive drop ($mb MB) -> $incomingDir"

    New-Item -ItemType Directory -Path $incomingDir -Force | Out-Null
    $destZip = Join-Path $incomingDir "tutorial-update.zip"
    $destSha = Join-Path $incomingDir "tutorial-update.sha256"
    $destReady = Join-Path $incomingDir "tutorial-update.ready"

    # Remove the ready marker first so the host cannot apply a half-copied zip.
    if (Test-Path -LiteralPath $destReady) {
        Remove-Item -LiteralPath $destReady -Force
    }

    Copy-Item -LiteralPath $zipPath -Destination $destZip -Force
    $hash = (Get-FileHash -LiteralPath $destZip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $destSha -Value $hash -Encoding ascii
    Set-Content -LiteralPath $destReady -Value ((Get-Date).ToUniversalTime().ToString("o")) -Encoding ascii

    Write-Step "Drop complete. Host supervisor will apply when Drive finishes syncing tutorial-update.ready"
    Write-Host "Package: $destZip"
    Write-Host "SHA256: $hash"
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
