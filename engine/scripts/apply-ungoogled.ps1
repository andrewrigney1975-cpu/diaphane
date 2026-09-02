# M1 phase 2: apply the ungoogled-chromium patch set to the checked-out tree.
# Run AFTER checkout.ps1 succeeds, BEFORE build.ps1.
$ErrorActionPreference = "Stop"
$root = "F:\cef-build"
$src  = "$root\chromium_git\chromium\src"
$ung  = "$root\ungoogled-chromium"

$env:PATH = "$root\depot_tools;$env:PATH"
$py = "python"

Write-Host "== prune binaries =="
& $py "$ung\utils\prune_binaries.py" $src "$ung\pruning.list"

Write-Host "== apply patches (111) =="
& $py "$ung\utils\patches.py" apply $src "$ung\patches"

Write-Host "== domain substitution =="
& $py "$ung\utils\domain_substitution.py" apply -r "$ung\domain_substitution.list" `
    -c "$root\domsub.cache" $src

Write-Host "== merge flags.gn -> cef GN_DEFINES fragment =="
Copy-Item "$ung\flags.gn" "$root\ungoogled-flags.gn" -Force

Write-Host "APPLY_DONE exit=$LASTEXITCODE"
