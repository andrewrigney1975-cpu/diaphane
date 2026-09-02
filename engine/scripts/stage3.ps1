# Stage 3: CEF project generation + build libcef (Release, x64).
# Uses CEF's Windows "custom toolchain" mode against the local VS 2026 + Win SDK 10.0.26100.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$cef  = "$src\cef"
$out  = "$src\out\Release_GN_x64"
$VS   = "F:\Program Files\Microsoft Visual Studio\18\Community"

# --- import x64 MSVC env (INCLUDE / LIB / PATH) from vcvars64.bat ---
cmd /c "`"$VS\VC\Auxiliary\Build\vcvars64.bat`" >nul 2>&1 && set" | ForEach-Object {
  if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "env:$($matches[1])" -Value $matches[2] }
}

$env:PATH = "$src\third_party\depot_tools;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"
$env:CEF_USE_GN = "1"

# CEF custom toolchain vars
$env:WIN_CUSTOM_TOOLCHAIN     = "1"
$env:CEF_VCVARS               = "none"   # env already imported above
$env:GYP_MSVS_OVERRIDE_PATH   = $VS
$env:GYP_MSVS_VERSION         = "2022"
$env:VS_CRT_ROOT              = "C:\Program Files (x86)\Windows Kits\10\Redist\10.0.26100.0\ucrt\DLLs\x64"
$env:SDK_ROOT                 = "C:\Program Files (x86)\Windows Kits\10"
$env:SDK_VERSION              = "10.0.26100.0"

$env:GN_DEFINES = "is_official_build=true proprietary_codecs=true ffmpeg_branding=Chrome " +
                  "enable_widevine=false use_thin_lto=false chrome_pgo_phase=0 " +
                  "symbol_level=1 blink_symbol_level=0"
$env:GN_ARGUMENTS = "--ide=none"

Set-Location $src
Write-Host "=== cef gclient_hook (create projects) ==="
& vpython3.bat "$cef\tools\gclient_hook.py" *> "$root\s3-hook.log"
Write-Host "hook exit=$LASTEXITCODE"
Get-Content "$root\s3-hook.log" -Tail 15

if (-not (Test-Path "$out\args.gn")) { Write-Host "STAGE3_DONE no_args_gn"; exit 1 }

Write-Host "=== append ungoogled flags.gn ==="
Add-Content "$out\args.gn" "`n# ---- ungoogled-chromium flags.gn ----`n"
Add-Content "$out\args.gn" (Get-Content "$root\ungoogled-flags.gn")
Copy-Item "$out\args.gn" "$root\final-args.gn" -Force

Write-Host "=== gn gen ==="
& gn gen $out *> "$root\s3-gngen.log"
Write-Host "gn gen exit=$LASTEXITCODE"
Get-Content "$root\s3-gngen.log" -Tail 25
if ($LASTEXITCODE -ne 0) { Write-Host "STAGE3_DONE gn_gen_failed"; exit 1 }

Write-Host "=== autoninja cef ==="
& autoninja -C $out cef *> "$root\s3-ninja.log"
$ne = $LASTEXITCODE
Get-Content "$root\s3-ninja.log" -Tail 30
Write-Host "STAGE3_DONE ninja_exit=$ne out=$out"
