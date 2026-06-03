Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Deprecated (Phase 9): superseded by scripts/measure-perf.ps1, whose `cold-start` scenario measures
# service-graph construction time + managed heap headlessly and cross-platform. This shim forwards to it and
# then re-emits the legacy { ElapsedSeconds; WorkingSetMB } object (mapped from the cold-start scenario) so
# existing callers that read those properties keep working.
Write-Warning 'measure-startup.ps1 is deprecated; use scripts/measure-perf.ps1 (cold-start scenario).'
# Route the forwarded probe's stdout to the host so the legacy { ElapsedSeconds; WorkingSetMB } object below
# is the sole pipeline output (matching the old contract), not buried in the probe's JSON.
& (Join-Path $PSScriptRoot 'measure-perf.ps1') | Out-Host

# measure-perf.ps1 writes artifacts/perf/perf.json relative to the current directory; we run in the same
# process so that path resolves identically here.
$reportPath = Join-Path 'artifacts/perf' 'perf.json'
if (-not (Test-Path $reportPath)) {
    Write-Warning "Perf report not found at $reportPath; cannot emit legacy startup object."
    return
}

$report = Get-Content $reportPath -Raw | ConvertFrom-Json
$coldStart = $report.scenarios | Where-Object { $_.name -eq 'cold-start' } | Select-Object -First 1
if (-not $coldStart) {
    Write-Warning 'cold-start scenario missing from perf report; cannot emit legacy startup object.'
    return
}

[PSCustomObject]@{
    ElapsedSeconds = [Math]::Round($coldStart.elapsedMs / 1000, 2)
    WorkingSetMB   = [Math]::Round($coldStart.workingSetBytes / 1MB, 1)
}
