# Stage 2 (tree now correctly pinned to 151.0.7922.174): hooks + ungoogled patches.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$ung  = "$root\ungoogled-chromium"
$env:PATH = "$src\third_party\depot_tools;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"
$env:PATCH_BIN = "C:\Program Files\Git\usr\bin\patch.exe"
$env:GYP_MSVS_OVERRIDE_PATH = "F:\Program Files\Microsoft Visual Studio\18\Community"
$env:GYP_MSVS_VERSION = "2022"
$env:vs2022_install = "F:\Program Files\Microsoft Visual Studio\18\Community"
$env:CEF_USE_GN = "1"
$py = "vpython3.bat"

Set-Location "$root\chromium_git\chromium"
Write-Host "=== gclient runhooks ==="
& gclient runhooks -j8 2>&1 | Select-Object -Last 4
Write-Host "runhooks exit=$LASTEXITCODE"

Write-Host "=== prune (best effort) ==="
& $py "$ung\utils\prune_binaries.py" $src "$ung\pruning.list" *> "$root\s2-prune.log"

Write-Host "=== apply patches ==="
& $py "$ung\utils\patches.py" apply $src "$ung\patches" *> "$root\s2-patches.log"
$pe = $LASTEXITCODE
Get-Content "$root\s2-patches.log" -Tail 6

Write-Host "=== domain substitution ==="
if (Test-Path "$root\domsub.tar.gz") { Remove-Item -Force "$root\domsub.tar.gz" }
& $py "$ung\utils\domain_substitution.py" apply -r "$ung\domain_regex.list" `
    -f "$ung\domain_substitution.list" -c "$root\domsub.tar.gz" $src *> "$root\s2-domsub.log"
$de = $LASTEXITCODE

Copy-Item "$ung\flags.gn" "$root\ungoogled-flags.gn" -Force
Write-Host "STAGE2_DONE patch_exit=$pe domsub_exit=$de"
