#!/usr/bin/env bash
set -euo pipefail

echo "Nuvio does not bundle mpv in Phase 2."
echo "Install mpv from https://mpv.io/installation/, a current package, or mpv-build; MPV Manager is optional setup-only."
echo "Nuvio must be pointed at the final mpv binary, not an mpv-manager helper."
echo "Set NUVIO_MPV_PATH to the full mpv path if automatic discovery does not find it."
