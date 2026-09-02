# M1 phase 1: check out Chromium 151 (CEF branch 7922) + CEF, no build yet.
$ErrorActionPreference = "Stop"
$root = "F:\cef-build"

$env:PATH = "$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:GYP_MSVS_OVERRIDE_PATH = "F:\Program Files\Microsoft Visual Studio\18\Community"
$env:GYP_MSVS_VERSION = "2022"
$env:vs2022_install = "F:\Program Files\Microsoft Visual Studio\18\Community"
$env:CEF_USE_GN = "1"

# bootstrap depot_tools (downloads its bundled git/python on first run)
& "$root\depot_tools\bootstrap\win_tools.bat"

python "$root\automate-git.py" `
  --download-dir="$root\chromium_git" `
  --depot-tools-dir="$root\depot_tools" `
  --branch=7922 `
  --x64-build `
  --no-debug-build `
  --no-build `
  --no-distrib

Write-Host "CHECKOUT_DONE exit=$LASTEXITCODE"
