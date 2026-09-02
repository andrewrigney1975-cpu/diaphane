# Clean redo: tree is tangled from partial unprune. Reset to pristine and do it
# right: full sync (deps incl. toolchains) -> hooks -> ungoogled WITH
# --keep-contingent-paths so clang/rust/ninja/siso are never pruned.
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

Write-Host "=== 1. reset src + depot_tools sub-repo to pristine ==="
git -C $src reset --hard refs/tags/151.0.7922.174 2>&1 | Select-Object -Last 1
git -C $dt reset --hard 2>&1 | Select-Object -Last 1
Remove-Item -Force "$root\domsub.tar.gz" -EA SilentlyContinue

Write-Host "=== 2. gclient sync -j1 (pinned, reset, restores ALL deps + toolchains) ==="
Set-Location "$root\chromium_git\chromium"
for ($i=1;$i -le 3;$i++){
  & gclient sync --revision "src@refs/tags/151.0.7922.174" --with_branch_heads --with_tags `
      --reset --force --delete_unversioned_trees --nohooks -j1
  if ($LASTEXITCODE -eq 0) { break }
  Write-Host "sync attempt $i exit $LASTEXITCODE"; Start-Sleep 20
}
Write-Host "sync exit=$LASTEXITCODE"

Write-Host "=== 3. runhooks -j1 ==="
& gclient runhooks -j1 *> "$root\cr-hooks.log"
Write-Host "runhooks exit=$LASTEXITCODE"

$clang = Test-Path "$src\third_party\llvm-build\Release+Asserts\bin\clang-cl.exe"
$rust  = Test-Path "$src\third_party\rust-toolchain\bin\rustc.exe"
$ninja = Test-Path "$src\third_party\ninja\ninja.exe"
Write-Host "toolchains: clang=$clang rust=$rust ninja=$ninja"

Write-Host "=== 4. ungoogled: prune --keep-contingent-paths ==="
& $py "$ung\utils\prune_binaries.py" $src "$ung\pruning.list" --keep-contingent-paths *> "$root\cr-prune.log"

Write-Host "=== 5. ungoogled: apply patches ==="
& $py "$ung\utils\patches.py" apply $src "$ung\patches" *> "$root\cr-patches.log"
$pe = $LASTEXITCODE

Write-Host "=== 6. ungoogled: domain substitution ==="
& $py "$ung\utils\domain_substitution.py" apply -r "$ung\domain_regex.list" `
    -f "$ung\domain_substitution.list" -c "$root\domsub.tar.gz" $src *> "$root\cr-domsub.log"
$de = $LASTEXITCODE
Copy-Item "$ung\flags.gn" "$root\ungoogled-flags.gn" -Force

$clang2 = Test-Path "$src\third_party\llvm-build\Release+Asserts\bin\clang-cl.exe"
$rust2  = Test-Path "$src\third_party\rust-toolchain\bin\rustc.exe"
Write-Host "CLEANREDO_DONE sync=$LASTEXITCODE patches=$pe domsub=$de clang=$clang2 rust=$rust2 ninja=$ninja"
