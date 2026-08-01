#Requires -Version 5.1
<#
.SYNOPSIS
  Build Content.Server, package it with Resources, upload to the tutorial host FileShare.

.DESCRIPTION
  Local-only deploy helper for DESKTOP-7SCFTK4 (192.168.1.22).
  Does not touch the remote MCP (port 8000) or FileShare server (port 8765).

  Package lands at:
    http://192.168.1.22:8765/incoming/tutorial-update.zip
  Plus a marker:
    incoming/tutorial-update.ready

  The host supervisor (Watch-TutorialServer.ps1) applies the package:
  stop SS14 -> extract -> start SS14 (infra stays up).

.EXAMPLE
  .\Deploy-TutorialHost.ps1

.EXAMPLE
  .\Deploy-TutorialHost.ps1 -SkipBuild -Configuration Release
#>
param(
    [string] $RepoRoot = "",
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [switch] $SkipBuild,
    [string] $BaseUrl = "http://192.168.1.22:8765",
    [string] $Token,
    [string] $TokenFile = "$env:USERPROFILE\.wizden-host-fileshare.token",
    [switch] $ResourcesOnly,
    [switch] $BinOnly
)

$ErrorActionPreference = "Stop"

if (-not $RepoRoot) {
    # tutorial-host/ -> _Functional/ -> Build/ -> repo root
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
}

function Write-Step([string]$msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}

if (-not $Token) {
    if (-not (Test-Path -LiteralPath $TokenFile)) {
        throw "Pass -Token or create $TokenFile with the host FileShare bearer token."
    }
    $Token = (Get-Content -LiteralPath $TokenFile -Raw).Trim()
}

$serverProj = Join-Path $RepoRoot "Content.Server\Content.Server.csproj"
$serverDll = Join-Path $RepoRoot "bin\Content.Server\Content.Server.dll"
$resourcesDir = Join-Path $RepoRoot "Resources"
$workRoot = Join-Path $env:TEMP ("wizden-tutorial-host-" + [guid]::NewGuid().ToString("n"))
$stageRoot = Join-Path $workRoot "stage"
$zipPath = Join-Path $workRoot "tutorial-update.zip"

try {
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

    if (-not $SkipBuild -and -not $ResourcesOnly) {
        Write-Step "Building Content.Server ($Configuration)"
        & dotnet build $serverProj -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }
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
        if ($LASTEXITCODE -ge 8) { throw "robocopy bin failed (exit $LASTEXITCODE)" }
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
    Write-Step "Uploading package ($mb MB)"

    function Send-FileSharePut([string]$RemoteName, [string]$LocalPath) {
        $uri = "$BaseUrl/$($RemoteName.TrimStart('/'))"
        $item = Get-Item -LiteralPath $LocalPath
        $request = [System.Net.HttpWebRequest]::Create($uri)
        $request.Method = "PUT"
        $request.Headers["Authorization"] = "Bearer $Token"
        $request.ContentType = "application/octet-stream"
        $request.AllowWriteStreamBuffering = $false
        $request.Timeout = 1000 * 60 * 120
        $request.ReadWriteTimeout = 1000 * 60 * 120
        $request.ContentLength = $item.Length
        $fs = [System.IO.File]::OpenRead($item.FullName)
        try {
            $reqStream = $request.GetRequestStream()
            try { $fs.CopyTo($reqStream, 1024 * 1024) }
            finally { $reqStream.Close() }
            $response = $request.GetResponse()
            try {
                $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
                $body = $reader.ReadToEnd()
                Write-Host $body
                if ([int]$response.StatusCode -ge 300) {
                    throw "Upload failed HTTP $([int]$response.StatusCode): $body"
                }
            }
            finally { $response.Close() }
        }
        finally { $fs.Close() }
    }

    Send-FileSharePut "incoming/tutorial-update.zip" $zipPath

    $readyPath = Join-Path $workRoot "tutorial-update.ready"
    Set-Content -LiteralPath $readyPath -Value ((Get-Date).ToUniversalTime().ToString("o")) -Encoding ascii
    Send-FileSharePut "incoming/tutorial-update.ready" $readyPath

    Write-Step "Upload complete. Host supervisor will apply when it sees tutorial-update.ready"
    Write-Host "Package: incoming/tutorial-update.zip"
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
