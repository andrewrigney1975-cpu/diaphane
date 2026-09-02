# Clean recovery: prune/patch/domsub were run before the toolchain finished
# downloading, tangling the tree. Reset to pristine pinned Chromium, sync, and
# run hooks FIRST so clang/rust download with real URLs. ungoogled stages come after.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$env:PATH = "$src\third_party\depot_tools;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"

Write-Host "=== 1. reset src to pristine 151.0.7922.174 (undoes prune/patch/domsub) ==="
Set-Location $src
git reset --hard refs/tags/151.0.7922.174 2>&1 | Select-Object -Last 2
# only clear obvious ungoogled leftovers; do NOT wipe gclient-managed sub-repos
Remove-Item -Recurse -Force "$src\..\domsub.tar.gz" -EA SilentlyContinue

Write-Host "=== 2. gclient sync (pinned, reset sub-repos) ==="
Set-Location "$root\chromium_git\chromium"
for ($i=1; $i -le 3; $i++) {
  & gclient sync --revision "src@refs/tags/151.0.7922.174" `
      --with_branch_heads --with_tags --reset --force --delete_unversioned_trees --nohooks -j8
  if ($LASTEXITCODE -eq 0) { break }
  Start-Sleep 15
}
Write-Host "sync exit=$LASTEXITCODE"

Write-Host "=== 3. gclient runhooks (downloads clang/rust/toolchain) ==="
& gclient runhooks -j8 *> "$root\rh-full.log"
Write-Host "runhooks exit=$LASTEXITCODE"
Get-Content "$root\rh-full.log" -Tail 10

$clang = Test-Path "$src\third_party\llvm-build\Release+Asserts\bin\clang-cl.exe"
Write-Host "RESET_HOOKS_DONE clang=$clang sync_ok=($LASTEXITCODE)"
