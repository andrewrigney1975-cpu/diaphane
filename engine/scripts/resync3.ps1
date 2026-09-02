# litert/src pulls Android prebuilts via git-LFS; the googlesource LFS mirror
# returns HTTP 405 on the batch API. We build Windows only and don't need those
# Android .so files -> skip LFS smudge so pointers are left in place.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$env:PATH = "$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"
$src = "$root\chromium_git\chromium\src"
Set-Location "$root\chromium_git\chromium"

git config --global filter.lfs.smudge "git-lfs smudge --skip -- %f"
git config --global filter.lfs.process "git-lfs filter-process --skip"
git config --global lfs.fetchexclude "*"

if (Test-Path "$src\third_party\litert\src") {
    Remove-Item -Recurse -Force "$src\third_party\litert\src" -EA SilentlyContinue
}

for ($i = 1; $i -le 4; $i++) {
    Write-Host "=== gclient sync (LFS-skip) attempt $i ==="
    & gclient sync --nohooks --with_branch_heads --with_tags --force --reset --delete_unversioned_trees -j8
    if ($LASTEXITCODE -eq 0) { Write-Host "RESYNC3_DONE exit=0"; exit 0 }
    Write-Host "attempt $i exit $LASTEXITCODE"
    Start-Sleep 15
}
Write-Host "RESYNC3_DONE exit=1"
