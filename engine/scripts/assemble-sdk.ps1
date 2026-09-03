# Manually assemble a CEF binary SDK for Diaphane.Core to link against.
# (make_distrib needs the full `cef` target incl. about_credits etc.; we built the
#  M1 subset, so assemble the standard layout by hand.)
$ErrorActionPreference = "Stop"
$out = "F:\cef-build\chromium_git\chromium\src\out\Release_GN_x64"
$cef = "F:\cef-build\chromium_git\chromium\src\cef"
$sdk = "F:\cef-build\dist\cef"

Remove-Item -Recurse -Force $sdk -EA SilentlyContinue
New-Item -ItemType Directory -Force "$sdk\Release","$sdk\Resources","$sdk\Resources\locales" | Out-Null

Write-Host "== headers =="
Copy-Item -Recurse "$cef\include" "$sdk\include"
Copy-Item -Recurse "$cef\libcef_dll" "$sdk\libcef_dll"

Write-Host "== Release binaries =="
$rel = @('libcef.dll','libcef.dll.lib','chrome_elf.dll','d3dcompiler_47.dll',
         'libEGL.dll','libGLESv2.dll','dxcompiler.dll','dxil.dll',
         'vk_swiftshader.dll','vulkan-1.dll','vk_swiftshader_icd.json',
         'snapshot_blob.bin','v8_context_snapshot.bin')
foreach ($f in $rel) { if (Test-Path "$out\$f") { Copy-Item "$out\$f" "$sdk\Release\" } else { Write-Host "  (missing $f)" } }
Copy-Item "$out\obj\cef\libcef_dll_wrapper.lib" "$sdk\Release\libcef_dll_wrapper.lib"

Write-Host "== Resources =="
Copy-Item "$out\icudtl.dat" "$sdk\Resources\"
Copy-Item "$out\resources.pak" "$sdk\Resources\"
Copy-Item "$out\chrome_100_percent.pak" "$sdk\Resources\"
Copy-Item "$out\chrome_200_percent.pak" "$sdk\Resources\"
if (Test-Path "$out\cef.pak")           { Copy-Item "$out\cef.pak" "$sdk\Resources\" }
if (Test-Path "$out\cef_extensions.pak") { Copy-Item "$out\cef_extensions.pak" "$sdk\Resources\" }
if (Test-Path "$out\devtools_resources.pak") { Copy-Item "$out\devtools_resources.pak" "$sdk\Resources\" }
Get-ChildItem "$out\locales\*.pak" -EA SilentlyContinue | Copy-Item -Destination "$sdk\Resources\locales\"

Write-Host "== manifest =="
$v = (Get-Item "$out\libcef.dll").VersionInfo.FileVersion
@"
Diaphane CEF SDK (M1)
CEF/Chromium: $v
target_cpu:   x64  |  config: Release (non-official)
codecs:       proprietary_codecs=true ffmpeg_branding=Chrome  (H.264/AAC + VP8/9/AV1/Opus)
base:         ungoogled-chromium 151.0.7922.173-1 (core patch subset; see engine/scripts/ungoogled-skipped.txt)
Assembled:    $(Get-Date -Format o)
"@ | Set-Content "$sdk\DIAPHANE_SDK.txt"

Write-Host "SDK_DONE"
Get-ChildItem $sdk -Recurse -File | Measure-Object Length -Sum |
  ForEach-Object { "files: $($_.Count)  size: {0:N1} MB" -f ($_.Sum/1MB) }
Get-ChildItem "$sdk\Release" | Select-Object Name,@{n='MB';e={[math]::Round($_.Length/1MB,2)}} | Format-Table -Auto
