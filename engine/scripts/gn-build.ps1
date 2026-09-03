# Continue from current tree state (CEF + 95 ungoogled + domsub applied,
# rlz/buildflags/buildflags.gni restored): gn gen -> autoninja cef.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$dt   = "$src\third_party\depot_tools"
$out  = "$src\out\Release_GN_x64"
$VS   = "F:\Program Files\Microsoft Visual Studio\18\Community"

cmd /c "`"$VS\VC\Auxiliary\Build\vcvars64.bat`" >nul 2>&1 && set" | ForEach-Object {
  if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "env:$($matches[1])" -Value $matches[2] }
}
$env:PATH = "$dt;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"; $env:DEPOT_TOOLS_UPDATE = "0"; $env:GIT_LFS_SKIP_SMUDGE = "1"
$env:CEF_USE_GN = "1"
$env:WIN_CUSTOM_TOOLCHAIN="1"; $env:CEF_VCVARS="none"
$env:GYP_MSVS_OVERRIDE_PATH=$VS; $env:GYP_MSVS_VERSION="2022"
$env:VS_CRT_ROOT="C:\Program Files (x86)\Windows Kits\10\Redist\10.0.26100.0\ucrt\DLLs\x64"
$env:SDK_ROOT="C:\Program Files (x86)\Windows Kits\10"; $env:SDK_VERSION="10.0.26100.0"

Set-Location $src
Write-Host "=== gn gen $out ==="
& gn gen $out *> "$root\gb-gngen.log"
Write-Host "gn gen exit=$LASTEXITCODE"
Get-Content "$root\gb-gngen.log" -Tail 20
if ($LASTEXITCODE -ne 0) { Write-Host "GNBUILD_DONE gn_gen_failed"; exit 1 }

Write-Host "=== autoninja -C $out cef  (the compile) ==="
$t0 = Get-Date
& autoninja -C $out cefsimple libcef libcef_dll_wrapper *> "$root\gb-ninja.log"
$ne = $LASTEXITCODE
Write-Host ("elapsed: {0:hh\:mm\:ss}" -f ((Get-Date)-$t0))
Get-Content "$root\gb-ninja.log" -Tail 30
Write-Host "GNBUILD_DONE ninja_exit=$ne"
if ($ne -eq 0) {
  Get-ChildItem "$out\libcef.dll","$out\cefsimple.exe","$out\cefclient.exe" -EA SilentlyContinue |
    Select-Object Name,@{n='MB';e={[math]::Round($_.Length/1MB,1)}}
}
