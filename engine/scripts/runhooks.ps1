# Stage 2a (retry): the CEF-pinned outer depot_tools (2026-06) is too old for
# Chromium 151's .vpython3 specs ("requires-python" field). Use the tree's own
# src/third_party/depot_tools (2026-09, DEPS-matched) to run Chromium hooks.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$env:PATH = "$src\third_party\depot_tools;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"
$env:GYP_MSVS_OVERRIDE_PATH = "F:\Program Files\Microsoft Visual Studio\18\Community"
$env:GYP_MSVS_VERSION = "2022"
$env:vs2022_install = "F:\Program Files\Microsoft Visual Studio\18\Community"
$env:CEF_USE_GN = "1"

Set-Location "$root\chromium_git\chromium"
Write-Host "=== gclient runhooks (tree depot_tools) ==="
& gclient runhooks -j8
Write-Host "RUNHOOKS_DONE exit=$LASTEXITCODE"
