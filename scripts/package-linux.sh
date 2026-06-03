#!/usr/bin/env bash
#
# package-linux.sh — publish, stage compliance, smoke, and produce the portable Linux
# tar.gz artifact.
#
# Usage: package-linux.sh [rid] [version]
#   rid     defaults to linux-x64
#   version defaults to <VersionPrefix> in Directory.Build.props
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

RID="${1:-linux-x64}"
VERSION="${2:-}"
if [ -z "$VERSION" ]; then
  VERSION="$(grep -oE '<VersionPrefix>[^<]+' Directory.Build.props | head -n1 | sed 's/<VersionPrefix>//')"
fi
[ -n "$VERSION" ] || { echo "package-linux: could not determine version." >&2; exit 1; }

PROJECT="src/Nuvio.Desktop/Nuvio.Desktop.csproj"
PUBLISH_DIR="$REPO_ROOT/artifacts/publish/$RID"
DIST_DIR="$REPO_ROOT/artifacts/dist"
ARTIFACT="$DIST_DIR/nuvio-desktop-$VERSION-$RID.tar.gz"

echo "==> Publishing $RID (version $VERSION)"
rm -rf "$PUBLISH_DIR"
dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" -o "$PUBLISH_DIR"

echo "==> Staging compliance files"
bash "$REPO_ROOT/scripts/lib/stage-compliance.sh" "$PUBLISH_DIR" "$REPO_ROOT"

echo "==> Launch/exit smoke (--self-check)"
chmod +x "$PUBLISH_DIR/Nuvio.Desktop" 2>/dev/null || true
HOST_ARCH="$(uname -m)"
case "$HOST_ARCH" in
  x86_64) HOST_RID="linux-x64" ;;
  aarch64|arm64) HOST_RID="linux-arm64" ;;
  *) HOST_RID="linux-unknown" ;;
esac
if [ "$RID" = "$HOST_RID" ]; then
  ( cd "$PUBLISH_DIR" && ./Nuvio.Desktop --self-check )
else
  echo "    skipped: published $RID does not match host $HOST_RID"
fi

echo "==> Creating tarball"
mkdir -p "$DIST_DIR"
rm -f "$ARTIFACT"
tar -czf "$ARTIFACT" -C "$PUBLISH_DIR" .

echo "Wrote $ARTIFACT"
