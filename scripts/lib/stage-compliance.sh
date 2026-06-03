#!/usr/bin/env bash
#
# stage-compliance.sh — copy the release-required legal/compliance files into a target
# directory (an artifact root or a macOS .app Resources folder). Shared by all per-OS
# packagers so the staged file set stays identical across platforms.
#
# Usage: stage-compliance.sh <target-dir> [repo-root]
#
set -euo pipefail

TARGET_DIR="${1:?usage: stage-compliance.sh <target-dir> [repo-root]}"
REPO_ROOT="${2:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"

# Keep this list in sync with:
#   - the <None Include> copy block in src/Nuvio.Desktop/Nuvio.Desktop.csproj
#   - the artifact-contents assertions in tests/Nuvio.Desktop.Tests
#   - the release.yml artifact-contents gate
COMPLIANCE_FILES=(
  "LICENSE"
  "NOTICE"
  "docs/dependency-licenses.md"
  "docs/source-availability.md"
  "docs/native-dependency-provenance.md"
)

mkdir -p "$TARGET_DIR"
for rel in "${COMPLIANCE_FILES[@]}"; do
  src="$REPO_ROOT/$rel"
  if [ ! -f "$src" ]; then
    echo "stage-compliance: required file missing: $src" >&2
    exit 1
  fi
  cp -f "$src" "$TARGET_DIR/$(basename "$rel")"
done

echo "Staged ${#COMPLIANCE_FILES[@]} compliance files into $TARGET_DIR"
