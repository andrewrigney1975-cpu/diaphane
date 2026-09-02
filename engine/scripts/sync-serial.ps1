$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$dt   = "$src\third_party\depot_tools"
$env:PATH = "$dt;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"

Write-Host "=== revert domain substitution inside src/third_party/depot_tools ==="
git -C $dt reset --hard 2>&1 | Select-Object -Last 1
git -C $dt clean -ffd 2>&1 | Select-Object -Last 1
Remove-Item -Recurse -Force "$dt\external_bin" -EA SilentlyContinue

Write-Host "=== also revert other separately-checked-out deps that domsub touched ==="
# domain substitution walked into every sub-repo; reset the ones with runtime python
foreach ($d in @("$src\tools\clang", "$src\third_party\catapult")) {
  if (Test-Path "$d\.git") { git -C $d reset --hard 2>&1 | Select-Object -Last 1 }
}

Set-Location "$root\chromium_git\chromium"
Write-Host "=== gclient sync -j1 (serial: no gsutil lock races) ==="
for ($i=1; $i -le 3; $i++) {
  & gclient sync --revision "src@refs/tags/151.0.7922.174" `
      --with_branch_heads --with_tags --reset --force --delete_unversioned_trees --nohooks -j1
  if ($LASTEXITCODE -eq 0) { break }
  Write-Host "sync attempt $i exit $LASTEXITCODE"
  Start-Sleep 20
}
$se = $LASTEXITCODE
Write-Host "sync exit=$se"

$clang = Test-Path "$src\third_party\llvm-build\Release+Asserts\bin\clang-cl.exe"
Write-Host "SYNCSERIAL_DONE sync_exit=$se clang=$clang"
