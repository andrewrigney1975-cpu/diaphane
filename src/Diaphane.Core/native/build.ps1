# Build DiaphaneCore.dll + diaphane_helper.exe against the CEF SDK.
# Uses the CMake + Ninja bundled with Visual Studio 2026.
param(
  [string]$Sdk = "F:\cef-build\dist\cef",
  [string]$Config = "Release"
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$VS   = "F:\Program Files\Microsoft Visual Studio\18\Community"
$cmake = "$VS\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$ninja = "$VS\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"

# import the x64 MSVC environment
cmd /c "`"$VS\VC\Auxiliary\Build\vcvars64.bat`" >nul 2>&1 && set" | ForEach-Object {
  if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "env:$($matches[1])" -Value $matches[2] }
}

$build = Join-Path $here "build"
& $cmake -S $here -B $build -G Ninja `
    "-DCMAKE_MAKE_PROGRAM=$ninja" `
    "-DCMAKE_BUILD_TYPE=$Config" `
    "-DDIAPHANE_CEF_SDK=$($Sdk -replace '\\','/')"
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }

& $cmake --build $build --config $Config
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "`n== staged in $build\bin ==" -ForegroundColor Green
Get-ChildItem "$build\bin\DiaphaneCore.dll","$build\bin\diaphane_helper.exe","$build\bin\libcef.dll" |
  Select-Object Name,@{n='MB';e={[math]::Round($_.Length/1MB,2)}}
