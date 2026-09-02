# ungoogled prune lists third_party/llvm-build in CONTINGENT_PATHS and removes it
# (ungoogled expects source-built clang). We want the prebuilt. Re-fetch the exact
# Win clang package pinned in DEPS and extract it.
$ErrorActionPreference = "Stop"
$src = "F:\cef-build\chromium_git\chromium\src"
$dst = "$src\third_party\llvm-build\Release+Asserts"
$rev = "llvmorg-23-init-19482-g53d18800-1"
$obj = "Win/clang-$rev.tar.xz"
$sha = "9e3894b94d0d5e3d5904a559f590f12bd53aa5d1d9b6a902de2acb957825de46"
$url = "https://commondatastorage.googleapis.com/chromium-browser-clang/$obj"
$tmp = "F:\cef-build\clang-win.tar.xz"

if (Test-Path "$dst\bin\clang-cl.exe") { Write-Host "clang already present"; exit 0 }

Write-Host "downloading $url"
Invoke-WebRequest -Uri $url -OutFile $tmp
$got = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLower()
if ($got -ne $sha) { throw "sha mismatch: got $got want $sha" }

New-Item -ItemType Directory -Force -Path $dst | Out-Null
Write-Host "extracting..."
& tar -xf $tmp -C $dst
# gclient's gcs dep drops a revision marker; mirror it so update.py version checks pass
Set-Content "$src\third_party\llvm-build\Release+Asserts\cr_build_revision" $rev -NoNewline
Remove-Item $tmp -Force

$ok = Test-Path "$dst\bin\clang-cl.exe"
Write-Host "RESTORE_CLANG_DONE clang=$ok"
& "$dst\bin\clang-cl.exe" --version 2>&1 | Select-Object -First 2
