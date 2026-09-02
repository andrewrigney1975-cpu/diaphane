$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$env:PATH = "$src\third_party\depot_tools;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"

# clear stale gsutil bootstrap lock from a killed process
$gs = "$src\third_party\depot_tools\external_bin\gsutil"
Remove-Item -Force "$gs\gsutil_5.35.locked" -EA SilentlyContinue
Remove-Item -Recurse -Force "$gs\gsutil_5.35" -EA SilentlyContinue

Set-Location "$root\chromium_git\chromium"
Write-Host "=== sync (pinned, -j4) ==="
for ($i=1; $i -le 4; $i++) {
  & gclient sync --revision "src@refs/tags/151.0.7922.174" `
      --with_branch_heads --with_tags --reset --force --delete_unversioned_trees --nohooks -j4
  if ($LASTEXITCODE -eq 0) { break }
  Write-Host "sync attempt $i exit $LASTEXITCODE"
  Start-Sleep 20
}
Write-Host "sync exit=$LASTEXITCODE"

Write-Host "=== runhooks -j4 ==="
& gclient runhooks -j4 *> "$root\sh2-hooks.log"
Write-Host "runhooks exit=$LASTEXITCODE"
iconv -f UTF-16LE -t UTF-8 "$root\sh2-hooks.log" 2>$null | Select-String "clang|llvm|Hook '" | Select-Object -Last 12

$clang = Test-Path "$src\third_party\llvm-build\Release+Asserts\bin\clang-cl.exe"
$rust  = Test-Path "$src\third_party\rust-toolchain\bin\rustc.exe"
Write-Host "SYNCHOOKS2_DONE clang=$clang rust=$rust"
