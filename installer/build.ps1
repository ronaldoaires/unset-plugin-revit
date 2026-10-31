<#
.SYNOPSIS
  Build the add-in for every supported Revit year and the Inno Setup installer.

.EXAMPLE
  .\installer\build.ps1                     # version from Directory.Build.props
  .\installer\build.ps1 -Version 1.0.1
  .\installer\build.ps1 -Years 2025,2026    # skip 2027 (needs the .NET 10 SDK)

  Output: installer\output\UnSetBIM-Setup-<version>.exe
#>
param(
    [string] $Version,
    [int[]]  $Years = @(2025, 2026, 2027)
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $Version) {
    [xml] $props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be MAJOR.MINOR.PATCH (got '$Version')." }

# Start clean so a year that is not built now is not packaged from an old build.
$artifacts = Join-Path $root 'artifacts'
Get-ChildItem $artifacts -Directory -Filter 'Release.R*' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

foreach ($year in $Years) {
    $configuration = "Release.R$($year.ToString().Substring(2))"
    Write-Host "== Building $configuration (v$Version)" -ForegroundColor Cyan
    dotnet build (Join-Path $root 'UnSetBim.slnx') -c $configuration "-p:Version=$Version" --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $configuration." }
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'ISCC.exe (Inno Setup 6) not found.' }

Write-Host "== Building installer" -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'UnSetBIM.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

Write-Host "Done: installer\output\UnSetBIM-Setup-$Version.exe" -ForegroundColor Green
