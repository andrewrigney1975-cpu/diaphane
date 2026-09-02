# M1 phase 3: generate projects and build libcef (Release, x64).
$ErrorActionPreference = "Stop"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"

$env:PATH = "$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:GYP_MSVS_OVERRIDE_PATH = "F:\Program Files\Microsoft Visual Studio\18\Community"
$env:GYP_MSVS_VERSION = "2022"
$env:vs2022_install = "F:\Program Files\Microsoft Visual Studio\18\Community"
$env:CEF_USE_GN = "1"

# CEF build config. ungoogled flags.gn is appended to args.gn after generation.
$env:GN_DEFINES = "is_official_build=true proprietary_codecs=true ffmpeg_branding=Chrome " +
                  "use_thin_lto=false enable_widevine=false safe_browsing_mode=0 " +
                  "use_official_google_api_keys=false chrome_pgo_phase=0"
$env:GN_ARGUMENTS = "--ide=none"

Push-Location $src
Write-Host "== cef_create_projects =="
& python "cef\tools\translator.py" --help *> $null   # noop warm
& python "$src\cef\tools\gclient_hook.py"

$out = "$src\out\Release_GN_x64"
if (Test-Path "$root\ungoogled-flags.gn") {
    Write-Host "== append ungoogled flags.gn to args.gn =="
    Add-Content "$out\args.gn" (Get-Content "$root\ungoogled-flags.gn")
    & gn gen $out
}

Write-Host "== ninja libcef =="
& autoninja -C $out cef
Write-Host "BUILD_DONE exit=$LASTEXITCODE  ->  $out"
Pop-Location
