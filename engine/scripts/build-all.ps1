# Full M1 build in ONE pass. CEF's patcher.py is NOT safe to re-run once ungoogled
# patches are layered on top, so gclient_hook runs exactly once (before ungoogled),
# then only plain `gn gen` / `autoninja` afterward.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$dt   = "$src\third_party\depot_tools"
$cef  = "$src\cef"
$ung  = "$root\ungoogled-chromium"
$out  = "$src\out\Release_GN_x64"
$VS   = "F:\Program Files\Microsoft Visual Studio\18\Community"

# import x64 MSVC env
cmd /c "`"$VS\VC\Auxiliary\Build\vcvars64.bat`" >nul 2>&1 && set" | ForEach-Object {
  if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "env:$($matches[1])" -Value $matches[2] }
}
$env:PATH = "$dt;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"
$env:PATCH_BIN = "C:\Program Files\Git\usr\bin\patch.exe"
$env:CEF_USE_GN = "1"
$env:WIN_CUSTOM_TOOLCHAIN   = "1"
$env:CEF_VCVARS             = "none"
$env:GYP_MSVS_OVERRIDE_PATH = $VS
$env:GYP_MSVS_VERSION       = "2022"
$env:VS_CRT_ROOT            = "C:\Program Files (x86)\Windows Kits\10\Redist\10.0.26100.0\ucrt\DLLs\x64"
$env:SDK_ROOT              = "C:\Program Files (x86)\Windows Kits\10"
$env:SDK_VERSION          = "10.0.26100.0"
# CEF requires enable_widevine=true / clang_use_chrome_plugins=false / optimize_webui=true
# (ungoogled flags.gn is already compatible). Keep our config minimal: codecs + siso.
# Non-official release build for the M1 gate (faster; official/LTO/PGO is a later pass).
$env:GN_DEFINES = "proprietary_codecs=true ffmpeg_branding=Chrome use_siso=true translate_genders=false"
if (Test-Path env:GN_ARGUMENTS) { Remove-Item env:GN_ARGUMENTS }
$py = "vpython3.bat"

Write-Host "=== 1. reset src pristine ==="
git -C $src reset --hard refs/tags/151.0.7922.174 2>&1 | Select-Object -Last 1
git -C $dt reset --hard 2>&1 | Select-Object -Last 1
& "$dt\bootstrap\win_tools.bat" *> $null

# Step 2 (prune) intentionally skipped: prune removes prebuilt libs the build needs
# (rust vendor .lib, .tlb, .dll) even with --keep-contingent-paths. Pruning only matters
# for redistribution licensing, not a local build. Privacy = patches + domsub + GN flags.
Write-Host "=== 2. prune SKIPPED (local build) ==="

Write-Host "=== 3. CEF gclient_hook (patcher + args.gn + gn gen) -- ONCE ==="
Set-Location $src
& $py "$cef\tools\gclient_hook.py" *> "$root\ba-hook.log"
Write-Host "gclient_hook exit=$LASTEXITCODE"
(Select-String -Path "$root\ba-hook.log" -Pattern 'patches total|failed to apply' | Select-Object -Last 3).Line

Write-Host "=== 4. ungoogled patches on top (fuzz 3, skip conflicts) ==="
# For a libcef build, skip the ungoogled *browser* flag system (add-flag-*,
# add-flags-*, the flag-infra headers/components) and a few pure-UI patches: they
# need chrome/browser/ungoogled/* + ungoogled_flag_entries.h wired into the
# ungoogled browser, don't apply cleanly under CEF-first, and leave orphaned code
# that fails to compile (e.g. toolbar_view.cc show_avatar_toolbar_button). Keep
# everything else in extra/ (webrtc-ip-policy, intranet-redirect-detector,
# default-prefs, disable-battery-status, updater-disable-auto-update, ...).
$denyRe = 'add-flag|add-flags-for|add-ungoogled-flag-headers|add-components-ungoogled|' +
          'add-credits|add-extra-channel-info|add-suggestions-url-field|first-run-page|' +
          'keep-expired-flags|remove-uneeded-ui|enable-menu-on-reload-button|' +
          'enable-paste-and-go-new-tab-button|restore-classic-ntp|disable-formatting-in-omnibox|' +
          'remove-unused-preferences-fields|move-js-optimizer-unfamiliar-sites|fix-building-without-safebrowsing|disable-rlz'
$applied=0; $skipped=@()
Get-Content "$ung\patches\series" | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
    $p = $_
    $pf = Join-Path "$ung\patches" $p
    if ($p -match $denyRe) { $skipped += $p; return }
    & $env:PATCH_BIN -p1 --forward --fuzz=3 --no-backup-if-mismatch -d $src -i $pf --dry-run *> $null
    if ($LASTEXITCODE -eq 0) { & $env:PATCH_BIN -p1 --forward --fuzz=3 --no-backup-if-mismatch -d $src -i $pf *> $null; $applied++ }
    else { $skipped += $p }
}
$skipped | Set-Content "$root\ungoogled-skipped.txt"
Write-Host "ungoogled applied=$applied skipped=$($skipped.Count)"

Write-Host "=== 5. domain substitution ==="
Remove-Item -Force "$root\domsub.tar.gz" -EA SilentlyContinue
& $py "$ung\utils\domain_substitution.py" apply -r "$ung\domain_regex.list" `
    -f "$ung\domain_substitution.list" -c "$root\domsub.tar.gz" $src *> "$root\ba-domsub.log"
Write-Host "domsub exit=$LASTEXITCODE"

Write-Host "=== 5b. diaphane build fixes (SDK 26100, ungoogled<->CEF, grd staleness) ==="
& powershell -ExecutionPolicy Bypass -File "$root\apply-diaphane-fixes.ps1" -src $src

Write-Host "=== 6. merge ungoogled flags.gn + re-gn-gen ==="
if (-not (Test-Path "$out\args.gn")) { Write-Host "BUILD_ALL_DONE no_args_gn (gclient_hook failed at step 3)"; exit 1 }
Add-Content "$out\args.gn" "`n# ---- ungoogled-chromium flags.gn ----"
# safe_browsing_mode=0 needs fix-building-without-safebrowsing.patch fully rebased
# onto Cr151 (123 hunks, doesn't apply clean under CEF-first) or the build fails on
# SBER_LEVEL_* / webstore refs. M1: let safe_browsing compile at CEF's default mode;
# it's disabled by the default-prefs patch and de-phoned by the iridium patches.
Add-Content "$out\args.gn" ((Get-Content "$ung\flags.gn") | Where-Object { $_ -notmatch 'safe_browsing_mode' })
Copy-Item "$out\args.gn" "$root\final-args.gn" -Force
& gn gen $out *> "$root\ba-gngen.log"
Write-Host "gn gen exit=$LASTEXITCODE"
Get-Content "$root\ba-gngen.log" -Tail 20
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD_ALL_DONE gn_gen_failed"; exit 1 }

Write-Host "=== 7. autoninja cef ==="
& autoninja -C $out cefsimple libcef libcef_dll_wrapper *> "$root\ba-ninja.log"
$ne = $LASTEXITCODE
Get-Content "$root\ba-ninja.log" -Tail 25
Write-Host "BUILD_ALL_DONE ninja_exit=$ne out=$out"
