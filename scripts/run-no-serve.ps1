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

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot

$AppArgs = @($args | ForEach-Object { if ($_ -eq '') { '""' } else { $_ } })

$DotNetArgs = @('run', '--project', 'UnoVibe/UnoVibe.csproj', '--framework', 'net10.0-desktop')
if ($AppArgs.Count -gt 0) {
    $DotNetArgs += '--'
    $DotNetArgs += $AppArgs
}

Start-Process -FilePath 'dotnet' `
    -ArgumentList $DotNetArgs `
    -WorkingDirectory $RepoRoot `
    -WindowStyle Hidden
