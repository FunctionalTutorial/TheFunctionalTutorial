#Requires -Version 5.1
<#
.SYNOPSIS
  Apply the host Drive heartbeat to ~/.cursor/mcp.json (windows-mcp-remote only).

.DESCRIPTION
  Reads host-status/heartbeat.json from the Drive drop. Updates url + Bearer token.
  Backs up mcp.json to mcp.json.bak first. Does not print the token.
#>
param(
    [string] $DropRoot = "",
    [string] $McpJsonPath = "",
    [int] $StaleMinutes = 15
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "Resolve-TutorialHostDropRoot.ps1")

function Write-Step([string]$msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}

$drop = Resolve-TutorialHostDropRoot -DropRoot $DropRoot
$heartbeatPath = Join-Path $drop "host-status\heartbeat.json"
$tokenPath = Join-Path $drop "host-status\mcp.token"
$urlPath = Join-Path $drop "host-status\mcp.url"

$heartbeat = $null
if (Test-Path -LiteralPath $heartbeatPath) {
    $heartbeat = Get-Content -LiteralPath $heartbeatPath -Raw -Encoding UTF8 | ConvertFrom-Json
}

$mcpUrl = $null
$mcpToken = $null
if ($heartbeat -and $heartbeat.mcp) {
    $mcpUrl = [string]$heartbeat.mcp.url
    $mcpToken = [string]$heartbeat.mcp.token
}
if ((-not $mcpToken) -and (Test-Path -LiteralPath $tokenPath)) {
    $mcpToken = (Get-Content -LiteralPath $tokenPath -Raw).Trim()
}
if ((-not $mcpUrl) -and (Test-Path -LiteralPath $urlPath)) {
    $mcpUrl = (Get-Content -LiteralPath $urlPath -Raw).Trim()
}
if (-not $mcpUrl -or -not $mcpToken) {
    throw "Missing MCP url/token in $heartbeatPath (or host-status/mcp.url + mcp.token). Is the host heartbeat watcher running?"
}
if (-not $heartbeat) {
    $heartbeat = [pscustomobject]@{
        updatedUtc = $null
        lanIp      = $null
        publicIp   = $null
        mcp        = [pscustomobject]@{ url = $mcpUrl; token = $mcpToken }
    }
}
else {
    if (-not $heartbeat.mcp) {
        $heartbeat | Add-Member -NotePropertyName mcp -NotePropertyValue ([pscustomobject]@{})
    }
    $heartbeat.mcp.url = $mcpUrl
    $heartbeat.mcp.token = $mcpToken
}

$updated = $null
if ($heartbeat.updatedUtc) {
    try { $updated = [datetime]::Parse($heartbeat.updatedUtc, $null, [System.Globalization.DateTimeStyles]::RoundtripKind) }
    catch { $updated = $null }
}
if ($updated) {
    $age = [datetime]::UtcNow - $updated.ToUniversalTime()
    if ($age.TotalMinutes -gt $StaleMinutes) {
        Write-Warning ("Heartbeat is {0:n1} minutes old (host Drive or announcer may be down)." -f $age.TotalMinutes)
    }
}

if (-not $McpJsonPath) {
    $McpJsonPath = Join-Path $env:USERPROFILE ".cursor\mcp.json"
}
if (-not (Test-Path -LiteralPath $McpJsonPath)) {
    throw "Missing Cursor MCP config: $McpJsonPath"
}

Write-Step "Updating windows-mcp-remote in $McpJsonPath"
$backup = "$McpJsonPath.bak"
Copy-Item -LiteralPath $McpJsonPath -Destination $backup -Force

$json = Get-Content -LiteralPath $McpJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $json.mcpServers) {
    $json | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([pscustomobject]@{})
}

$auth = "Bearer $($heartbeat.mcp.token)"
$entry = $json.mcpServers.'windows-mcp-remote'
if (-not $entry) {
    $json.mcpServers | Add-Member -NotePropertyName 'windows-mcp-remote' -NotePropertyValue ([pscustomobject]@{
            url     = [string]$heartbeat.mcp.url
            type    = "http"
            headers = [pscustomobject]@{ Authorization = $auth }
        })
}
else {
    $entry.url = [string]$heartbeat.mcp.url
    if (-not $entry.type) {
        $entry | Add-Member -NotePropertyName type -NotePropertyValue "http"
    }
    else {
        $entry.type = "http"
    }
    if (-not $entry.headers) {
        $entry | Add-Member -NotePropertyName headers -NotePropertyValue ([pscustomobject]@{ Authorization = $auth })
    }
    else {
        $entry.headers.Authorization = $auth
    }
}

$json | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $McpJsonPath -Encoding UTF8

Write-Host "MCP url: $($heartbeat.mcp.url)"
Write-Host "LAN: $($heartbeat.lanIp)  public: $($heartbeat.publicIp)"
Write-Host "Backup: $backup"
Write-Host "Reload MCP in Cursor if windows-mcp-remote is already connected."
