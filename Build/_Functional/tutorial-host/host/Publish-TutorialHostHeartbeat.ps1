#Requires -Version 5.1
<#
.SYNOPSIS
  Write host-status/heartbeat.json into the Drive drop (IP + MCP token).

.DESCRIPTION
  Outbound-only. Discovers the current LAN/public IP and reads the windows-mcp
  auth_key. Laptop Sync-TutorialHostMcp.ps1 applies that to ~/.cursor/mcp.json.
  Does not print the token.
#>
param(
    [switch] $Once
)

# When dotted into Watch-TutorialServer, do not change the caller's ErrorActionPreference.
if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
}
. "$PSScriptRoot\HostPaths.ps1"

function Get-TutorialHostLanIp {
    $candidates = @()
    try {
        $candidates = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object {
                $_.IPAddress -and
                $_.IPAddress -notlike '127.*' -and
                $_.IPAddress -notlike '169.254.*'
            } |
            Select-Object -ExpandProperty IPAddress)
    }
    catch { }

    if (-not $candidates -or $candidates.Count -eq 0) {
        foreach ($line in (& ipconfig.exe)) {
            if ($line -match 'IPv4 Address[^:]*:\s*(\d+\.\d+\.\d+\.\d+)') {
                $ip = $Matches[1]
                if ($ip -notlike '127.*' -and $ip -notlike '169.254.*') {
                    $candidates += $ip
                }
            }
        }
    }

    foreach ($ip in $candidates) {
        if ($ip -like '192.168.*') { return $ip }
    }
    foreach ($ip in $candidates) {
        if ($ip -like '10.*') { return $ip }
    }
    if ($candidates -and $candidates.Count -gt 0) { return $candidates[0] }
    return $null
}

function Get-TutorialHostPublicIp {
    if ($script:CachedPublicIp -and $script:CachedPublicIpAt) {
        $age = [datetime]::UtcNow - $script:CachedPublicIpAt
        if ($age.TotalMinutes -lt 10) {
            return $script:CachedPublicIp
        }
    }

    try {
        $ip = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 10).Trim()
        if ($ip -match '^\d+\.\d+\.\d+\.\d+$') {
            $script:CachedPublicIp = $ip
            $script:CachedPublicIpAt = [datetime]::UtcNow
            return $ip
        }
    }
    catch { }

    if (Test-Path -LiteralPath $HeartbeatJson) {
        try {
            $prev = Get-Content -LiteralPath $HeartbeatJson -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($prev.publicIp) { return [string]$prev.publicIp }
        }
        catch { }
    }
    return $null
}

function Get-WindowsMcpAuthKey {
    $cfg = Join-Path $env:USERPROFILE '.windows-mcp\config.toml'
    if (-not (Test-Path -LiteralPath $cfg)) {
        throw "Missing windows-mcp config: $cfg"
    }
    $raw = Get-Content -LiteralPath $cfg -Raw
    $m = [regex]::Match($raw, '(?im)^\s*auth_key\s*=\s*"?([^"\r\n]+)"?')
    if (-not $m.Success) {
        throw "auth_key not found in $cfg"
    }
    return $m.Groups[1].Value.Trim()
}

function Publish-TutorialHostHeartbeat {
    $lan = Get-TutorialHostLanIp
    if (-not $lan) {
        throw 'Could not discover a LAN IPv4 address'
    }
    $public = Get-TutorialHostPublicIp
    $token = Get-WindowsMcpAuthKey

    $payload = [ordered]@{
        schemaVersion = 1
        updatedUtc    = [datetime]::UtcNow.ToString('o')
        hostname      = $env:COMPUTERNAME
        lanIp         = $lan
        publicIp      = $public
        mcp           = [ordered]@{
            url   = "http://${lan}:8000/mcp"
            token = $token
        }
        ss14          = [ordered]@{
            lan    = "ss14://${lan}:1212"
            public = $(if ($public) { "ss14://${public}:1212" } else { $null })
        }
    }

    $statusDir = Split-Path -Parent $HeartbeatJson
    New-Item -ItemType Directory -Path $statusDir -Force | Out-Null

    $tokenChanged = $true
    $identityChanged = $true
    if (Test-Path -LiteralPath $HeartbeatJson) {
        try {
            $prev = Get-Content -LiteralPath $HeartbeatJson -Raw -Encoding UTF8 | ConvertFrom-Json
            $tokenChanged = [string]$prev.mcp.token -ne $token
            $identityChanged = $tokenChanged -or
                [string]$prev.lanIp -ne $lan -or
                [string]$prev.publicIp -ne [string]$public -or
                [string]$prev.mcp.url -ne [string]$payload.mcp.url
            if (-not $identityChanged) {
                $prevUtc = $null
                if ($prev.updatedUtc) {
                    $prevUtc = [datetime]::Parse($prev.updatedUtc, $null, [System.Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
                }
                if ($prevUtc -and (([datetime]::UtcNow - $prevUtc).TotalMinutes -lt 10)) {
                    return
                }
            }
        }
        catch { }
    }

    $tmp = "$HeartbeatJson.tmp"
    ($payload | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $tmp -Encoding UTF8
    Move-Item -LiteralPath $tmp -Destination $HeartbeatJson -Force

    # Sidecars so the laptop can pull a regenerated bearer without parsing JSON.
    if ($tokenChanged -or -not (Test-Path -LiteralPath $McpTokenFile)) {
        $tmpTok = "$McpTokenFile.tmp"
        Set-Content -LiteralPath $tmpTok -Value $token -Encoding ascii -NoNewline
        Move-Item -LiteralPath $tmpTok -Destination $McpTokenFile -Force
    }
    $url = [string]$payload.mcp.url
    if ($identityChanged -or -not (Test-Path -LiteralPath $McpUrlFile)) {
        $tmpUrl = "$McpUrlFile.tmp"
        Set-Content -LiteralPath $tmpUrl -Value $url -Encoding ascii -NoNewline
        Move-Item -LiteralPath $tmpUrl -Destination $McpUrlFile -Force
    }
}

if ($MyInvocation.InvocationName -ne '.' -or $Once) {
    Publish-TutorialHostHeartbeat
}
