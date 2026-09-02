# CEF and ungoogled patch sets overlap (~crash, gcm, content, chrome/ui).
# Correct order: pristine -> CEF patches (via gclient_hook, the supported path)
# -> ungoogled patches on top with fuzz, skipping the ones that still conflict
# (those become the documented "patch rebase" backlog) -> domain substitution.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$dt   = "$src\third_party\depot_tools"
$cef  = "$src\cef"
$ung  = "$root\ungoogled-chromium"
$VS   = "F:\Program Files\Microsoft Visual Studio\18\Community"
$env:PATH = "$dt;$root\depot_tools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:DEPOT_TOOLS_UPDATE = "0"
$env:GIT_LFS_SKIP_SMUDGE = "1"
$env:PATCH_BIN = "C:\Program Files\Git\usr\bin\patch.exe"
$env:GYP_MSVS_OVERRIDE_PATH = $VS
$env:GYP_MSVS_VERSION = "2022"
$env:vs2022_install = $VS
$env:CEF_USE_GN = "1"
$py = "vpython3.bat"

Write-Host "=== 1. reset src to pristine (keep deps/toolchains) ==="
git -C $src reset --hard refs/tags/151.0.7922.174 2>&1 | Select-Object -Last 1

Write-Host "=== 2. prune --keep-contingent-paths ==="
& $py "$ung\utils\prune_binaries.py" $src "$ung\pruning.list" --keep-contingent-paths *> "$root\cf-prune.log"

Write-Host "=== 3. CEF gclient_hook (applies CEF patches + generates projects) ==="
Set-Location $src
& $py "$cef\tools\gclient_hook.py" *> "$root\cf-hook.log"
$hookExit = $LASTEXITCODE
Get-Content "$root\cf-hook.log" -Tail 12
Write-Host "gclient_hook exit=$hookExit"

Write-Host "=== 4. ungoogled patches on top (fuzz 3, skip conflicts) ==="
$applied = 0; $skipped = @()
Get-Content "$ung\patches\series" | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
    $pf = Join-Path "$ung\patches" $_
    & $env:PATCH_BIN -p1 --forward --fuzz=3 --no-backup-if-mismatch -d $src -i $pf --dry-run 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        & $env:PATCH_BIN -p1 --forward --fuzz=3 --no-backup-if-mismatch -d $src -i $pf 2>&1 | Out-Null
        $applied++
    } else {
        $skipped += $_
    }
}
Write-Host "ungoogled applied=$applied skipped=$($skipped.Count)"
$skipped | ForEach-Object { Write-Host "  SKIP $_" }
$skipped | Set-Content "$root\ungoogled-skipped.txt"

Write-Host "=== 5. domain substitution ==="
Remove-Item -Force "$root\domsub.tar.gz" -EA SilentlyContinue
& $py "$ung\utils\domain_substitution.py" apply -r "$ung\domain_regex.list" `
    -f "$ung\domain_substitution.list" -c "$root\domsub.tar.gz" $src *> "$root\cf-domsub.log"
$dsExit = $LASTEXITCODE

Copy-Item "$ung\flags.gn" "$root\ungoogled-flags.gn" -Force
Write-Host "CEFFIRST_DONE hook=$hookExit ung_applied=$applied ung_skipped=$($skipped.Count) domsub=$dsExit"
