#requires -Version 5.1
<#
.SYNOPSIS
    Launch the UnoVibe desktop app in the background without managing a server.
.DESCRIPTION
    Equivalent of scripts/run-no-serve.sh: runs `dotnet run` detached, discarding
    console output. With no launch-target argument the app shows ConnectPage.
    Any arguments given to this script are forwarded to the app (a folder path or
    an http(s) server URL, plus optional --password). Example:
    scripts/run-no-serve.ps1 http://localhost:4196
#>

$ErrorActionPreference = 'Stop'

# Resolve the repo root (parent of the scripts/ directory) so the relative
# project path works no matter where this script is invoked from.
$RepoRoot = Split-Path -Parent $PSScriptRoot

# Quote empty values (e.g. `--password ""`) so Start-Process keeps them as an
# empty argument instead of dropping them.
$AppArgs = @($args | ForEach-Object { if ($_ -eq '') { '""' } else { $_ } })

$DotNetArgs = @('run', '--project', 'UnoVibe/UnoVibe.csproj', '--framework', 'net10.0-desktop')
if ($AppArgs.Count -gt 0) {
    # Everything after `--` is handed to the app, not to `dotnet run`.
    $DotNetArgs += '--'
    $DotNetArgs += $AppArgs
}

Start-Process -FilePath 'dotnet' `
    -ArgumentList $DotNetArgs `
    -WorkingDirectory $RepoRoot `
    -WindowStyle Hidden
