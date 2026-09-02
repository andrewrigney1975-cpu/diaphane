$ErrorActionPreference = "Stop"
$src = "F:\cef-build\chromium_git\chromium\src"
$dst = "$src\third_party\rust-toolchain"
$rev = "b998449636a48e2c4a362809085b600a0174e1f2-2-llvmorg-23-init-19482-g53d18800"
$obj = "Win/rust-toolchain-$rev.tar.xz"
$sha = "482c5a8946869aae595adc7361b619e8af62dc002cd8f3a00b27fe59db4d74f3"
$url = "https://commondatastorage.googleapis.com/chromium-browser-clang/$obj"
$tmp = "F:\cef-build\rust-win.tar.xz"

if (Test-Path "$dst\bin\rustc.exe") { Write-Host "rust already present"; exit 0 }
Write-Host "downloading rust toolchain (~416 MB)..."
Invoke-WebRequest -Uri $url -OutFile $tmp
$got = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLower()
if ($got -ne $sha) { throw "sha mismatch: $got vs $sha" }
New-Item -ItemType Directory -Force -Path $dst | Out-Null
& tar -xf $tmp -C $dst
# rust package ships a VERSION file; gclient normally writes cr_build_revision
Set-Content "$dst\INSTALLED_VERSION" $rev -NoNewline
Remove-Item $tmp -Force
$ok = Test-Path "$dst\bin\rustc.exe"
Write-Host "RESTORE_RUST_DONE rust=$ok"
if ($ok) { & "$dst\bin\rustc.exe" --version 2>&1 | Select-Object -First 1 }
