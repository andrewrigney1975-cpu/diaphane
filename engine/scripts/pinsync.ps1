# Repin: earlier bare `gclient sync` let src roll to Chromium main (155).
# CEF branch 7922 requires exactly refs/tags/151.0.7922.174. Reset everything to it.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$env:PATH = "$src\third_party\depot_tools;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"

Set-Location $src
Write-Host "== drop ungoogled patch edits + fetch tag =="
git reset --hard HEAD
git clean -ffd -e out -e third_party/depot_tools | Out-Null
git fetch origin +refs/tags/151.0.7922.174:refs/tags/151.0.7922.174 --no-tags 2>&1 | Select-Object -Last 3

Set-Location "$root\chromium_git\chromium"
for ($i = 1; $i -le 3; $i++) {
    Write-Host "== gclient sync --revision src@refs/tags/151.0.7922.174 (attempt $i) =="
    & gclient sync --revision "src@refs/tags/151.0.7922.174" `
        --with_branch_heads --with_tags --force --reset --delete_unversioned_trees --nohooks -j8
    if ($LASTEXITCODE -eq 0) { break }
    Start-Sleep 15
}
$sync = $LASTEXITCODE

Set-Location $src
$v = (Get-Content chrome\VERSION) -join '.'
Write-Host "PINSYNC_DONE sync_exit=$sync version=$v"
