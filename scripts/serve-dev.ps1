#requires -Version 5.1
<#
.SYNOPSIS
    Start the dev `opencode serve` on http://localhost:4196 if it isn't already running.
.DESCRIPTION
    See AGENTS.md "How to Build & Run".
#>

$ErrorActionPreference = 'Stop'

$Port = 4196
$LogDir = Join-Path $env:LOCALAPPDATA 'opencode'
$Log = Join-Path $LogDir 'serve_dev.log'
$ErrLog = Join-Path $LogDir 'serve_dev.err.log'

function Test-OpencodeServeRunning {
    # A process matching "opencode serve" is already running...
    $proc = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like '*opencode serve*' }
    if ($null -ne $proc) { return $true }

    # ...or the health endpoint responds (process may exist but be unhealthy).
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:${Port}/global/health" -UseBasicParsing -TimeoutSec 2
        return $resp.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

if (Test-OpencodeServeRunning) {
    Write-Host "opencode serve already running on http://localhost:${Port}"
    exit 0
}

Write-Host "Starting opencode serve on port ${Port}..."
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Process -FilePath 'opencode' `
    -ArgumentList @('serve', '--port', "$Port") `
    -RedirectStandardOutput $Log `
    -RedirectStandardError $ErrLog `
    -WindowStyle Hidden

# Wait for readiness.
for ($i = 1; $i -le 30; $i++) {
    if (Test-OpencodeServeRunning) {
        Write-Host "Readiness confirmed on http://localhost:${Port} after ${i}s"
        exit 0
    }
    Start-Sleep -Seconds 1
}

[Console]::Error.WriteLine("ERROR: server did not become healthy within 30s. Check $Log")
exit 1
