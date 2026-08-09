#requires -Version 5.1
<#
.SYNOPSIS
    Launch the UnoVibe desktop app in the background without managing a server.
.DESCRIPTION
    Equivalent of scripts/run-no-serve.sh: runs `dotnet run` detached, discarding
    console output. With no launch-target argument the app shows ConnectPage.
    Pass a folder path or server URL as the first argument to open it directly.
#>

$ErrorActionPreference = 'Stop'

# Resolve the repo root (parent of the scripts/ directory) so the relative
# project path works no matter where this script is invoked from.
$RepoRoot = Split-Path -Parent $PSScriptRoot

Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', 'UnoVibe/UnoVibe.csproj', '--framework', 'net10.0-desktop') `
    -WorkingDirectory $RepoRoot `
    -WindowStyle Hidden
