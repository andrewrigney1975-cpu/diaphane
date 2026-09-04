<#
.SYNOPSIS
  Build a self-contained, unpackaged diaphane you can zip and copy to another machine.

.DESCRIPTION
  1. Publishes Diaphane.App (win-x64, self-contained, no framework install needed).
  2. Copies the engine payload (DiaphaneCore.dll, libcef.dll, the helper, *.pak,
     icudtl.dat, snapshots, ANGLE/SwiftShader, locales/) next to the exe, so
     CefHost.ResolveNativeBinDir finds it beside the app.
  3. Zips the result as dist/diaphane-<version>-win-x64.zip.

  Run engine/ + src/Diaphane.Core/native/build.ps1 first — this script does not
  build the engine.
#>
param(
    [string]$Configuration = "Release",
    [string]$MSBuild = "F:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$app = Join-Path $repo "src\Diaphane.App\Diaphane.App.csproj"
$nativeBin = Join-Path $repo "src\Diaphane.Core\native\build\bin"

if (-not (Test-Path (Join-Path $nativeBin "DiaphaneCore.dll"))) {
    throw "Engine payload not found at $nativeBin. Build it first: src\Diaphane.Core\native\build.ps1"
}

# --- version ---
[xml]$csproj = Get-Content $app
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
if (-not $version) { $version = "0.0.0" }
Write-Host "Packaging diaphane $version ($Configuration)" -ForegroundColor Cyan

$stage = Join-Path $repo "dist\diaphane-$version-win-x64"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# --- publish the app ---
& $MSBuild $app /t:Publish /p:Configuration=$Configuration /p:Platform=x64 `
    /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:PublishDir="$stage\" /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }

# --- engine payload ---
$skip = @("*.lib", "*.log", "*.exp", "*.pdb")
Get-ChildItem $nativeBin -Recurse -File | ForEach-Object {
    foreach ($p in $skip) { if ($_.Name -like $p) { return } }
    $rel = $_.FullName.Substring($nativeBin.Length).TrimStart('\')
    $dst = Join-Path $stage $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    Copy-Item $_.FullName $dst -Force
}

# --- zip ---
$zip = Join-Path $repo "dist\diaphane-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "`n  $zip  ($mb MB)" -ForegroundColor Green
Write-Host "  unpacked: $stage" -ForegroundColor Green
