#!/usr/bin/env bash
set -euo pipefail

echo "Nuvio does not bundle mpv in Phase 2."
echo "Install mpv from https://mpv.io/installation/ or use MPV Manager as an optional setup helper."
echo "Nuvio must be pointed at the final mpv binary, not an mpv-manager helper."
echo "Set NUVIO_MPV_PATH to the full mpv path if automatic discovery does not find it."
