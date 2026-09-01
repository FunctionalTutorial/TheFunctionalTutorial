#Requires -Version 5.1
# Applies incoming/tutorial-update.zip: wait for Drive sync, stop SS14 -> extract -> start SS14+relay.
# Never touches MCP (8000), FileShare (8765), or host SQLite (D:\WizdenHost\data
# and live\bin\Content.Server\data). Do not robocopy /MIR.
param(
    [switch] $SkipStart,
    [int] $StableSeconds = 20,
    [int] $WaitTimeoutMinutes = 30
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\HostPaths.ps1"

function Write-LastApply([string]$Status, [string]$Detail) {
    $dir = Split-Path -Parent $LastApplyJson
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $payload = [ordered]@{
        status     = $Status
        detail     = $Detail
        updatedUtc = [datetime]::UtcNow.ToString('o')
        zipPath    = $IncomingZip
        zipBytes   = $(if (Test-Path -LiteralPath $IncomingZip) { (Get-Item -LiteralPath $IncomingZip).Length } else { $null })
    }
    $tmp = "$LastApplyJson.tmp"
    ($payload | ConvertTo-Json) | Set-Content -LiteralPath $tmp -Encoding UTF8
    Move-Item -LiteralPath $tmp -Destination $LastApplyJson -Force
}

function Wait-DriveZipStable {
    $deadline = (Get-Date).AddMinutes($WaitTimeoutMinutes)
    $lastSize = -1L
    $stableSince = $null
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-Path -LiteralPath $IncomingZip)) {
            $lastSize = -1L
            $stableSince = $null
            Start-Sleep -Seconds 2
            continue
        }
        $size = (Get-Item -LiteralPath $IncomingZip).Length
        if ($size -gt 0 -and $size -eq $lastSize) {
            if ($null -eq $stableSince) { $stableSince = Get-Date }
            elseif (((Get-Date) - $stableSince).TotalSeconds -ge $StableSeconds) {
                return $size
            }
        }
        else {
            $lastSize = $size
            $stableSince = $null
        }
        Start-Sleep -Seconds 2
    }
    throw "Timed out waiting for $IncomingZip to finish syncing"
}

if (-not (Test-Path -LiteralPath $IncomingZip)) {
    throw "Missing package: $IncomingZip"
}

if (Test-Path -LiteralPath $ApplyLockFile) {
    $age = (Get-Date) - (Get-Item $ApplyLockFile).LastWriteTime
    if ($age.TotalMinutes -lt 30) {
        throw "Apply already in progress (lock $ApplyLockFile)"
    }
    Remove-Item -LiteralPath $ApplyLockFile -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Path $StateRoot, $StagingRoot, $PackagesRoot, $LiveRoot, $LogsRoot -Force | Out-Null
Set-Content -LiteralPath $ApplyLockFile -Value (Get-Date).ToUniversalTime().ToString('o') -Encoding ascii
Set-Content -LiteralPath $DisableRestartFlag -Value 'apply' -Encoding ascii

try {
    Write-Host '=== Apply Tutorial Update ==='
    Write-Host "Package: $IncomingZip"

    Write-Host "Waiting for Drive zip to stabilize (${StableSeconds}s)..."
    $size = Wait-DriveZipStable
    Write-Host "Zip stable at $size bytes"

    if (-not (Test-Path -LiteralPath $IncomingSha256)) {
        Write-LastApply 'failed' 'Missing tutorial-update.sha256'
        throw "Missing hash file: $IncomingSha256"
    }
    $expected = ((Get-Content -LiteralPath $IncomingSha256 -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $IncomingZip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        if (Test-Path -LiteralPath $IncomingReady) {
            Remove-Item -LiteralPath $IncomingReady -Force -ErrorAction SilentlyContinue
        }
        Write-LastApply 'failed' "SHA256 mismatch expected=$expected actual=$actual"
        throw "SHA256 mismatch for $IncomingZip"
    }
    Write-Host "SHA256 ok: $actual"

    & "$PSScriptRoot\Stop-Ss14.ps1"

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $archiveZip = Join-Path $PackagesRoot "tutorial-update-$stamp.zip"
    Copy-Item -LiteralPath $IncomingZip -Destination $archiveZip -Force

    if (Test-Path -LiteralPath $StagingRoot) {
        Remove-Item -LiteralPath $StagingRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null

    Write-Host 'Extracting package...'
    & tar.exe -xf $IncomingZip -C $StagingRoot
    if ($LASTEXITCODE -ne 0) { throw "tar extract failed (exit $LASTEXITCODE)" }

    $stagedBin = Join-Path $StagingRoot 'bin\Content.Server'
    $stagedClient = Join-Path $StagingRoot 'bin\Content.Client'
    $stagedRes = Join-Path $StagingRoot 'Resources'
    $hasBin = Test-Path -LiteralPath (Join-Path $stagedBin 'Content.Server.dll')
    $hasClient = Test-Path -LiteralPath (Join-Path $stagedClient 'Content.Client.dll')
    $hasRes = Test-Path -LiteralPath $stagedRes

    if (-not $hasBin -and -not $hasRes -and -not $hasClient) {
        throw 'Package contained neither bin/Content.Server, bin/Content.Client, nor Resources'
    }

    if ($hasBin) {
        $stagedData = Join-Path $stagedBin 'data'
        if (Test-Path -LiteralPath $stagedData) {
            Write-Warning "Package contains bin/Content.Server/data - skipping (host SQLite is $DataRoot; do not /MIR)"
        }
        Write-Host 'Syncing bin/Content.Server -> live (excluding data/ SQLite and server_config.local.toml)'
        $liveBin = Join-Path $LiveRoot 'bin\Content.Server'
        New-Item -ItemType Directory -Path $liveBin -Force | Out-Null
        $binCopyArgs = @(
            $stagedBin, $liveBin, '/E'
        ) + $ContentServerRobocopyExcludes + @(
            '/NFL', '/NDL', '/NJH', '/NJS', '/nc', '/ns', '/np'
        )
        & robocopy.exe @binCopyArgs | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy bin failed (exit $LASTEXITCODE)" }
    }

    if ($hasClient) {
        Write-Host 'Syncing bin/Content.Client -> live'
        $liveClient = Join-Path $LiveRoot 'bin\Content.Client'
        New-Item -ItemType Directory -Path $liveClient -Force | Out-Null
        & robocopy.exe $stagedClient $liveClient /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy Content.Client failed (exit $LASTEXITCODE)" }
    }

    if ($hasRes) {
        Write-Host 'Syncing Resources -> live'
        $liveRes = Join-Path $LiveRoot 'Resources'
        New-Item -ItemType Directory -Path $liveRes -Force | Out-Null
        & robocopy.exe $stagedRes $liveRes /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy Resources failed (exit $LASTEXITCODE)" }
    }

    $stagedRt = Join-Path $StagingRoot 'RobustToolbox\Resources'
    if (Test-Path -LiteralPath $stagedRt) {
        Write-Host 'Syncing RobustToolbox/Resources -> live'
        $liveRt = Join-Path $LiveRoot 'RobustToolbox\Resources'
        New-Item -ItemType Directory -Path $liveRt -Force | Out-Null
        & robocopy.exe $stagedRt $liveRt /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy RobustToolbox/Resources failed (exit $LASTEXITCODE)" }
    }

    if (Test-Path (Join-Path $StagingRoot 'deploy-manifest.json')) {
        Copy-Item (Join-Path $StagingRoot 'deploy-manifest.json') (Join-Path $LiveRoot 'deploy-manifest.json') -Force
    }

    foreach ($path in @($IncomingReady, $IncomingZip, $IncomingSha256)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host "Archived package copy: $archiveZip"

    if (-not $SkipStart) {
        & "$PSScriptRoot\Start-Ss14.ps1"
    }
    else {
        Write-Host 'SkipStart set - SS14 not restarted.'
    }

    Write-LastApply 'ok' "Applied $archiveZip"
    Write-Host '=== Apply complete ==='
}
catch {
    Write-LastApply 'failed' $_.Exception.Message
    throw
}
finally {
    Remove-Item -LiteralPath $DisableRestartFlag -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $ApplyLockFile -Force -ErrorAction SilentlyContinue
}
