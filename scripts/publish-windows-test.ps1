$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot\UnoVibe
dotnet publish -c Release -f net10.0-windows10.0.26100.0 -r win-x64 --self-contained true -p:PublishAot=false -o $RepoRoot\artifacts\publish
Set-Location $RepoRoot
winapp package $RepoRoot\artifacts\publish --manifest .\Windows.Package.appxmanifest --output .\artifacts --cert devcert.pfx 