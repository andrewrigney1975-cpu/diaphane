# Stage 2b (correct order): hooks first, THEN ungoogled prune/patch/domsub.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$dt   = "$src\third_party\depot_tools"
$ung  = "$root\ungoogled-chromium"
$env:PATH = "$dt;$root\depot_tools;$env:PATH"
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
Write-Host "=== gclient runhooks -j1 ==="
& gclient runhooks -j1 *> "$root\s2b-hooks.log"
Write-Host "runhooks exit=$LASTEXITCODE"
$rc = Test-Path "$src\build\toolchain\win\rc\win\rc.exe"
Write-Host "rc.exe present: $rc"

Write-Host "=== ungoogled: prune (best effort) ==="
& $py "$ung\utils\prune_binaries.py" $src "$ung\pruning.list" *> "$root\s2b-prune.log"

Write-Host "=== ungoogled: apply 109 patches ==="
& $py "$ung\utils\patches.py" apply $src "$ung\patches" *> "$root\s2b-patches.log"
$pe = $LASTEXITCODE
Write-Host "patches exit=$pe"

Write-Host "=== ungoogled: domain substitution ==="
Remove-Item -Force "$root\domsub.tar.gz" -EA SilentlyContinue
& $py "$ung\utils\domain_substitution.py" apply -r "$ung\domain_regex.list" `
    -f "$ung\domain_substitution.list" -c "$root\domsub.tar.gz" $src *> "$root\s2b-domsub.log"
$de = $LASTEXITCODE
Write-Host "domsub exit=$de"

Copy-Item "$ung\flags.gn" "$root\ungoogled-flags.gn" -Force
$clang = Test-Path "$src\third_party\llvm-build\Release+Asserts\bin\clang-cl.exe"
Write-Host "STAGE2B_DONE hooks_rc=$rc patches=$pe domsub=$de clang=$clang"
