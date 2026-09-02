$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$dt   = "$src\third_party\depot_tools"
$env:PATH = "$dt;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"

$before = (git -C $src diff --shortstat 2>$null)
Write-Host "src diff BEFORE: $before"

Set-Location "$root\chromium_git\chromium"
Write-Host "=== gclient sync -j1 --nohooks (no reset/force: keep ungoogled patches) ==="
& gclient sync --nohooks -j1 *> "$root\rninja.log"
Write-Host "sync exit=$LASTEXITCODE"

$after = (git -C $src diff --shortstat 2>$null)
Write-Host "src diff AFTER:  $after"

$ninja = (Test-Path "$src\third_party\ninja\ninja.exe")
$siso  = (Test-Path "$src\third_party\siso\cipd\siso.exe")
$clang = (Test-Path "$src\third_party\llvm-build\Release+Asserts\bin\clang-cl.exe")
$rust  = (Test-Path "$src\third_party\rust-toolchain\bin\rustc.exe")
Write-Host "RESTORE_NINJA_DONE ninja=$ninja siso=$siso clang=$clang rust=$rust"
