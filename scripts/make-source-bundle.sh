#!/usr/bin/env bash
#
# make-source-bundle.sh — produce a reproducible GPL source tarball from HEAD.
#
# Uses `git archive`, so the tarball honors .gitignore: the git-ignored upstream/
# reference checkouts and build outputs are excluded automatically. The archive is
# deterministic for a given commit (git archive does not embed a wall-clock timestamp;
# entry times come from the commit), which keeps releases reproducible.
#
# Usage: make-source-bundle.sh [version]
#   version defaults to the <VersionPrefix> in Directory.Build.props.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

VERSION="${1:-}"
if [ -z "$VERSION" ]; then
  VERSION="$(grep -oE '<VersionPrefix>[^<]+' Directory.Build.props | head -n1 | sed 's/<VersionPrefix>//')"
fi
if [ -z "$VERSION" ]; then
  echo "make-source-bundle: could not determine version." >&2
  exit 1
fi

PREFIX="nuvio-desktop-${VERSION}/"
OUT_DIR="$REPO_ROOT/artifacts/source"
OUT="$OUT_DIR/nuvio-desktop-src-${VERSION}.tar.gz"

mkdir -p "$OUT_DIR"
git archive --format=tar.gz --prefix="$PREFIX" -o "$OUT" HEAD

echo "Wrote $OUT"
echo "Top-level entries:"
tar -tzf "$OUT" | sed "s#^${PREFIX}##" | awk -F/ 'NF>0 && $1!="" {print $1}' | sort -u | sed 's/^/  /'

if tar -tzf "$OUT" | grep -q "^${PREFIX}upstream/"; then
  echo "make-source-bundle: upstream/ leaked into the source bundle." >&2
  exit 1
fi
echo "Verified: upstream/ is excluded."
