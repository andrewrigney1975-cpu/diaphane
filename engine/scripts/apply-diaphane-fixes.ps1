# diaphane M1 build fixes, applied after ungoogled patches + domain substitution.
# Idempotent. Covers SDK 10.0.26100 mc.exe behavior, ungoogled<->CEF interactions,
# and CEF 7922 grd staleness vs Chromium 151.
param([string]$src = "F:\cef-build\chromium_git\chromium\src")

function Patch-File($path, $find, $replace, $label) {
  $full = Join-Path $src $path
  $t = Get-Content $full -Raw
  if ($t.Contains($replace)) { Write-Host "  [skip] $label (already applied)"; return }
  if (-not $t.Contains($find)) { Write-Host "  [WARN] $label : anchor not found"; return }
  Set-Content $full ($t.Replace($find, $replace)) -NoNewline
  Write-Host "  [ok]   $label"
}

Write-Host "== 1. mc.exe SDK 26100 extra .bin (both message_compiler.py copies) =="
$mcOld = "diff = filecmp.dircmp(tmp_dir, source)`n    if diff.diff_files or set(diff.left_list) != set(diff.right_list):"
$mcNew = "diff = filecmp.dircmp(tmp_dir, source)`n    _nb = lambda l: set(f for f in l if not f.lower().endswith('.bin'))`n    _dnb = [f for f in diff.diff_files if not f.lower().endswith('.bin')]`n    if _dnb or _nb(diff.left_list) != _nb(diff.right_list):"
Patch-File "build\win\message_compiler.py" $mcOld $mcNew "chromium message_compiler.py"
$mcOld2 = "        diff = filecmp.dircmp(tmp_dir, source)`n        if diff.diff_files or set(diff.left_list) != set(diff.right_list):"
$mcNew2 = "        diff = filecmp.dircmp(tmp_dir, source)`n        _nb = lambda l: set(f for f in l if not f.lower().endswith('.bin'))`n        _dnb = [f for f in diff.diff_files if not f.lower().endswith('.bin')]`n        if _dnb or _nb(diff.left_list) != _nb(diff.right_list):"
Patch-File "third_party\dawn\third_party\directx-shader-compiler\build\message_compiler.py" $mcOld2 $mcNew2 "dawn dxc message_compiler.py"

Write-Host "== 2. domain_reliability whitelist (ungoogled domsub vs configs) =="
Patch-File "components\domain_reliability\bake_in_configs.py" `
  "  return any(domain == e or domain.endswith('.' + e)  for e in DOMAIN_WHITELIST)" `
  "  return origin.startswith('https://') and origin.endswith('/')  # diaphane M1" `
  "bake_in_configs.py whitelist"

Write-Host "== 3. grit --assert-file-list tolerate equal-set/unequal-order =="
Patch-File "tools\grit\grit\tool\build.py" `
  "      error = '''Asserted file list does not match." `
  "      if not missing and not extra and not duplicates:`n        return True  # diaphane M1: set matches, order artifact`n      error = '''Asserted file list does not match." `
  "grit build.py assert"

Write-Host "== 4. alink /llvmlibempty (empty safe_browsing.lib under /WX) =="
Patch-File "build\toolchain\win\toolchain.gni" `
  "command = `"`$linker_wrapper`$lib \`"/OUT:{{output}}\`" /nologo {{arflags}} \`"@`$rspfile\`"`"" `
  "command = `"`$linker_wrapper`$lib \`"/OUT:{{output}}\`" /nologo /llvmlibempty {{arflags}} \`"@`$rspfile\`"`"" `
  "toolchain.gni alink"

Write-Host "== 5. cef_strings.grd: add Cr151 locales missing from CEF 7922 grd =="
$grd = Join-Path $src "cef\libcef\resources\cef_strings.grd"
$g = Get-Content $grd -Raw
if ($g.Contains("diaphane M1")) { Write-Host "  [skip] cef_strings.grd (already applied)" }
else {
  $locs = @("as","az","be","cy","fr-CA","is","kk","km","ky","lo","mk","mn","my","ne","or","pa","si","sq","sr-Latn","uz","zh-HK","zu","bs","eu","gl","hy","ka")
  $block = "    <!-- diaphane M1: Chromium 151 platform_pak_locales missing from CEF 7922 grd (English fallback) -->`n"
  $block += ($locs | ForEach-Object { "    <output filename=`"cef_strings_$_.pak`" type=`"data_package`" lang=`"$_`" />" }) -join "`n"
  Set-Content $grd ($g.Replace("  </outputs>", $block + "`n  </outputs>", 1)) -NoNewline
  Write-Host "  [ok]   cef_strings.grd (+$($locs.Count) locales)"
}

Write-Host "DIAPHANE_FIXES_DONE"
