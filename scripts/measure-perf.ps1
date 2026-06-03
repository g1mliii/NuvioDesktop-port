Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Phase 9 cross-platform performance/memory probe (9.1, 9.2). Builds Release and runs the in-app
# --perf-probe headless mode, which boots the real service graph and reports elapsed time + managed-heap
# deltas per scenario as JSON. Same code path runs on Windows, macOS, and Linux (see measure-perf.sh).
#
# Optional environment knobs (all have defaults):
#   NUVIO_PERF_CATALOG_ITEMS   catalog-scroll fixture size (default 5000)
#   NUVIO_PERF_DETAILS_LOOPS   details open/close iterations (default 200)
#   NUVIO_RUN_MPV_INTEGRATION  set to 1 (with mpv installed) to run the playback-stop loop
#   NUVIO_PERF_MEDIA_URL       real stream URL for mpv cache evidence (otherwise a generated WAV is used)

$project = 'src/Nuvio.Desktop/Nuvio.Desktop.csproj'
$outDir = 'artifacts/perf'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$out = Join-Path $outDir 'perf.json'

# $ErrorActionPreference='Stop' does not trip on a native exe's non-zero exit, so check $LASTEXITCODE
# explicitly — otherwise a failed build would fall through to a stale --no-build run reported as success.
dotnet build $project -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

dotnet run --project $project -c Release --no-build -- --perf-probe --perf-out $out
if ($LASTEXITCODE -ne 0) { throw "perf probe failed with exit code $LASTEXITCODE" }

Write-Host ''
Write-Host "Perf report written to $out"
