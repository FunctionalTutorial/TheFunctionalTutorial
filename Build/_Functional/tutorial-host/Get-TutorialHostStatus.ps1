#Requires -Version 5.1
<#
.SYNOPSIS
  Print Tutorial host heartbeat / incoming / last-apply status (no secrets).
#>
param(
    [string] $DropRoot = "",
    [int] $StaleMinutes = 15
)

$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "Resolve-TutorialHostDropRoot.ps1")

function Write-RedactedToken([string]$token) {
    if (-not $token) { return "(none)" }
    if ($token.Length -le 8) { return "***" }
    return $token.Substring(0, 4) + "..." + $token.Substring($token.Length - 4)
}

try {
    $drop = Resolve-TutorialHostDropRoot -DropRoot $DropRoot
}
catch {
    Write-Host "Drop root: $($_.Exception.Message)" -ForegroundColor Yellow
    exit 1
}

Write-Host "Drop root: $drop"
$heartbeatPath = Join-Path $drop "host-status\heartbeat.json"
$incomingDir = Join-Path $drop "incoming"
$lastApplyPath = Join-Path $drop "logs\last-apply.json"

if (-not (Test-Path -LiteralPath $heartbeatPath)) {
    Write-Host "Heartbeat: missing ($heartbeatPath)"
}
else {
    $hb = Get-Content -LiteralPath $heartbeatPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $ageText = "unknown"
    if ($hb.updatedUtc) {
        try {
            $updated = [datetime]::Parse($hb.updatedUtc, $null, [System.Globalization.DateTimeStyles]::RoundtripKind)
            $age = [datetime]::UtcNow - $updated.ToUniversalTime()
            $ageText = "{0:n1} min" -f $age.TotalMinutes
            if ($age.TotalMinutes -gt $StaleMinutes) {
                $ageText += " (STALE)"
            }
        }
        catch { }
    }
    Write-Host "Heartbeat: $($hb.updatedUtc)  age=$ageText  host=$($hb.hostname)"
    Write-Host "LAN: $($hb.lanIp)  public: $($hb.publicIp)"
    Write-Host "MCP url: $($hb.mcp.url)"
    Write-Host "MCP token: $(Write-RedactedToken $hb.mcp.token)"
    $tokFile = Join-Path $drop "host-status\mcp.token"
    $urlFile = Join-Path $drop "host-status\mcp.url"
    Write-Host "Sidecar mcp.url: $(Test-Path -LiteralPath $urlFile)  mcp.token: $(Test-Path -LiteralPath $tokFile)"
    Write-Host "SS14 lan: $($hb.ss14.lan)  public: $($hb.ss14.public)"
}

Write-Host "Incoming:"
if (Test-Path -LiteralPath $incomingDir) {
    Get-ChildItem -LiteralPath $incomingDir -Force | ForEach-Object {
        Write-Host ("  {0}  {1:n1} MB  {2:u}" -f $_.Name, ($_.Length / 1MB), $_.LastWriteTimeUtc)
    }
}
else {
    Write-Host "  (missing $incomingDir)"
}

if (-not (Test-Path -LiteralPath $lastApplyPath)) {
    Write-Host "Last apply: missing"
}
else {
    Write-Host "Last apply:"
    Get-Content -LiteralPath $lastApplyPath -Raw -Encoding UTF8
}
