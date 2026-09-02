# Stage 2b: apply the ungoogled-chromium patch set to the checked-out tree.
$ErrorActionPreference = "Continue"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$ung  = "$root\ungoogled-chromium"
$env:PATH = "$src\third_party\depot_tools;$root\depot_tools;$env:PATH"
$env:GIT_LFS_SKIP_SMUDGE = "1"
$py = "vpython3.bat"

Write-Host "== prune binaries =="
& $py "$ung\utils\prune_binaries.py" $src "$ung\pruning.list" 2>&1 | Tee-Object "$root\ung-prune.log"

Write-Host "== apply patches (111) =="
& $py "$ung\utils\patches.py" apply $src "$ung\patches" 2>&1 | Tee-Object "$root\ung-patches.log"
$patch_exit = $LASTEXITCODE

Write-Host "== domain substitution =="
& $py "$ung\utils\domain_substitution.py" apply -r "$ung\domain_substitution.list" `
    -c "$root\domsub.cache" $src 2>&1 | Tee-Object "$root\ung-domsub.log"

Copy-Item "$ung\flags.gn" "$root\ungoogled-flags.gn" -Force
Write-Host "APPLY_DONE patch_exit=$patch_exit final_exit=$LASTEXITCODE"
