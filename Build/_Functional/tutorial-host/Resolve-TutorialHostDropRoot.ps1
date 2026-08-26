#Requires -Version 5.1
<#
.SYNOPSIS
  Resolve the local Google Drive drop folder for Tutorial host deploys.

.DESCRIPTION
  Reads -DropRoot, or %USERPROFILE%\.wizden-host-drive.path (one line, no quotes).
  That file is created once after Drive for desktop mirrors WizdenHostDrop.
  Dot-source this script to get Resolve-TutorialHostDropRoot, or run it to print the path.
#>
param(
    [string] $DropRoot = ""
)

function Resolve-TutorialHostDropRoot {
    param(
        [string] $DropRoot = ""
    )

    $resolved = $DropRoot
    if (-not $resolved) {
        $pathFile = Join-Path $env:USERPROFILE '.wizden-host-drive.path'
        if (-not (Test-Path -LiteralPath $pathFile)) {
            throw @"
Missing $pathFile
Create it with one line: the local mirrored Google Drive folder for WizdenHostDrop.
Example: C:\Users\$env:USERNAME\My Drive\drive-drop
Use Mirror / Available offline, not stream placeholders.
"@
        }
        $resolved = (Get-Content -LiteralPath $pathFile -Raw).Trim().Trim('"')
    }

    if (-not $resolved) {
        throw "Drop root is empty."
    }
    if (-not (Test-Path -LiteralPath $resolved)) {
        throw "Drop root does not exist: $resolved"
    }

    return (Resolve-Path -LiteralPath $resolved).Path
}

if ($MyInvocation.InvocationName -ne '.') {
    Resolve-TutorialHostDropRoot -DropRoot $DropRoot
}
