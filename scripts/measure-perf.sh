#!/usr/bin/env bash
set -euo pipefail

# Phase 9 cross-platform performance/memory probe (9.1, 9.2). Builds Release and runs the in-app
# --perf-probe headless mode, which boots the real service graph and reports elapsed time + managed-heap
# deltas per scenario as JSON. Same code path runs on Windows, macOS, and Linux (see measure-perf.ps1).
#
# Optional environment knobs (all have defaults):
#   NUVIO_PERF_CATALOG_ITEMS   catalog-scroll fixture size (default 5000)
#   NUVIO_PERF_DETAILS_LOOPS   details open/close iterations (default 200)
#   NUVIO_RUN_MPV_INTEGRATION  set to 1 (with mpv installed) to run the playback-stop loop
#   NUVIO_PERF_MEDIA_URL       real stream URL for mpv cache evidence (otherwise a generated WAV is used)

project="src/Nuvio.Desktop/Nuvio.Desktop.csproj"
out_dir="artifacts/perf"
mkdir -p "$out_dir"
out="$out_dir/perf.json"

dotnet build "$project" -c Release
dotnet run --project "$project" -c Release --no-build -- --perf-probe --perf-out "$out"

echo ""
echo "Perf report written to $out"
