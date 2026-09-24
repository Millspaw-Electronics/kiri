<#
.SYNOPSIS
    Builds the KiRI Windows installer.

.DESCRIPTION
    Publishes the KiRI app and kiri-cli as self-contained x64 programs (so users
    don't need to install .NET) into windows\publish, then compiles the Inno Setup
    installer into windows\dist\KiRI-Setup-<version>.exe.

    Needs the .NET 8 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Version 1.1.0
#>
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$publish = Join-Path $root "publish"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

foreach ($project in "Kiri.App", "Kiri.Cli") {
    Write-Host "Publishing $project"
    dotnet publish (Join-Path $root "$project\$project.csproj") -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -p:DebugType=none -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project failed" }
}

$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup" }

Write-Host "Building the installer"
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $root "installer\KiRI.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

Get-Item (Join-Path $root "dist\KiRI-Setup-$Version.exe")
