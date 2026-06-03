#!/usr/bin/env bash
set -euo pipefail

dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build

for path in \
  LICENSE \
  NOTICE \
  docs/legal-compliance.md \
  docs/dependency-licenses.md \
  docs/architecture.md \
  docs/platform-matrix.md \
  docs/player-integration.md \
  docs/mpv-setup.md \
  docs/performance-budget.md \
  docs/perf/README.md \
  docs/perf/perf-report-template.md \
  docs/perf/baseline-2026-06.md \
  docs/regression-checklist.md \
  docs/phase-1-behavior-mapping.md \
  docs/player-parity-notes.md \
  docs/packaging.md \
  docs/source-availability.md \
  docs/native-dependency-provenance.md \
  docs/adr/0001-rebuild-desktop-ui-in-avalonia.md; do
  if [ ! -f "$path" ]; then
    echo "Missing required handoff file: $path" >&2
    exit 1
  fi
done
