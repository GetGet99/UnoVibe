#requires -Version 5.1
<#
.SYNOPSIS
    Validate that markdown files under the project keep lines under 150 characters.
.DESCRIPTION
    Enforces the AGENTS.md rule across AGENTS.md and agents-doc/*.md. Run before
    finishing any change that touched those files:
        scripts/validate-markdown-lines.ps1
    Without arguments the whole doc set is checked; file/folder arguments (repo
    root relative) restrict the check. Exit code 0 = all clean, 1 = violations.
#>

$ErrorActionPreference = 'Stop'

$MaxLength = 150
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Checked = 0
$Violations = 0

function Get-TargetFiles {
    $paths = @($args)
    if ($paths.Count -eq 0) {
        $docs = @(Get-Item -LiteralPath (Join-Path $RepoRoot 'AGENTS.md'))
        $docs += Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'agents-doc') `
            -File -Filter '*.md'
        return $docs
    }
    $result = @()
    foreach ($p in $paths) {
        $full = if ([System.IO.Path]::IsPathRooted($p)) {
            $p
        }
        else {
            Join-Path $RepoRoot $p
        }
        if (-not (Test-Path -LiteralPath $full)) {
            [Console]::Error.WriteLine("ERROR: not found: $p")
            exit 2
        }
        $item = Get-Item -LiteralPath $full
        if ($item.PSIsContainer) {
            $result += Get-ChildItem -LiteralPath $full -File -Filter '*.md'
        }
        else {
            $result += $item
        }
    }
    return $result
}

foreach ($file in (Get-TargetFiles @args | Sort-Object FullName)) {
    $Checked++
    $lineNo = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $lineNo++
        if ($line.Length -gt $MaxLength) {
            $rel = if ($file.FullName.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                $file.FullName.Substring($RepoRoot.Length + 1)
            }
            else {
                $file.FullName
            }
            [Console]::Error.WriteLine("${rel}:${lineNo}: line is $($line.Length) characters (max $MaxLength)")
            $Violations++
        }
    }
}

if ($Violations -gt 0) {
    [Console]::Error.WriteLine(
        "FAIL: $Violations line(s) too long across $Checked file(s). Wrap them and re-run.")
    exit 1
}
Write-Host "OK: $Checked markdown file(s) checked, every line under $MaxLength characters."