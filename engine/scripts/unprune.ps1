# Restore every file ungoogled's prune deleted (~247k), keeping the 109 patches +
# domain substitution (those are modifications, not deletions). Pruning only matters
# for redistribution licensing; for a local build the restored files are harmless and
# they unbreak `gclient sync` (DEPS parsing reads pruned version_file paths).
$ErrorActionPreference = "Continue"
$src = "F:\cef-build\chromium_git\chromium\src"
Set-Location $src

Write-Host "deleted before: " (git ls-files --deleted | Measure-Object).Count
git ls-files --deleted > "$env:TEMP\del.txt"
$total = (Get-Content "$env:TEMP\del.txt").Count
Write-Host "restoring $total files in batches..."
$batch = New-Object System.Collections.Generic.List[string]
$done = 0
foreach ($f in Get-Content "$env:TEMP\del.txt") {
    $batch.Add($f)
    if ($batch.Count -ge 2000) {
        git checkout -- @($batch) 2>$null
        $done += $batch.Count; $batch.Clear()
        Write-Host "  $done / $total"
    }
}
if ($batch.Count) { git checkout -- @($batch) 2>$null }

$del = (git ls-files --deleted | Measure-Object).Count
$mod = (git ls-files --modified | Measure-Object).Count
Write-Host "UNPRUNE_DONE deleted=$del modified=$mod"
