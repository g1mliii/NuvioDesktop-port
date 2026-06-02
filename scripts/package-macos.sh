#!/usr/bin/env bash
#
# package-macos.sh — publish, assemble a Nuvio Desktop.app bundle, stage compliance,
# smoke, and produce a DMG. Signs + notarizes when the signing env vars are present;
# otherwise emits unsigned artifacts (the CI path).
#
# Usage: package-macos.sh [rid] [version]
#   rid     defaults to osx-arm64 (pass osx-x64 to reuse the logic)
#   version defaults to <VersionPrefix> in Directory.Build.props
#
# Signing env vars (all required to trigger signing/notarization):
#   NUVIO_MACOS_DEV_ID       "Developer ID Application: Name (TEAMID)"
#   NUVIO_MACOS_TEAM_ID      Apple Developer Team ID
#   NUVIO_MACOS_AC_PASSWORD  app-specific password, OR a notarytool keychain profile name
#   NUVIO_MACOS_AC_APPLE_ID  (optional) Apple ID email; when set, use id/team/password auth,
#                            otherwise NUVIO_MACOS_AC_PASSWORD is treated as --keychain-profile
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

RID="${1:-osx-arm64}"
VERSION="${2:-}"
if [ -z "$VERSION" ]; then
  VERSION="$(grep -oE '<VersionPrefix>[^<]+' Directory.Build.props | head -n1 | sed 's/<VersionPrefix>//')"
fi
[ -n "$VERSION" ] || { echo "package-macos: could not determine version." >&2; exit 1; }

PROJECT="src/Nuvio.Desktop/Nuvio.Desktop.csproj"
PUBLISH_DIR="$REPO_ROOT/artifacts/publish/$RID"
DIST_DIR="$REPO_ROOT/artifacts/dist"
APP_DIR="$DIST_DIR/Nuvio Desktop.app"
DMG="$DIST_DIR/nuvio-desktop-$VERSION-$RID.dmg"
EXECUTABLE="Nuvio.Desktop"

echo "==> Publishing $RID (version $VERSION)"
rm -rf "$PUBLISH_DIR"
dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" -o "$PUBLISH_DIR"

echo "==> Assembling .app bundle"
rm -rf "$APP_DIR"
CONTENTS="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RES_DIR="$CONTENTS/Resources"
mkdir -p "$MACOS_DIR" "$RES_DIR"

# Published payload goes into Contents/MacOS.
cp -R "$PUBLISH_DIR"/. "$MACOS_DIR"/
chmod +x "$MACOS_DIR/$EXECUTABLE" 2>/dev/null || true

# Render Info.plist from the template with the release version.
sed "s/@VERSION@/$VERSION/g" "$REPO_ROOT/packaging/macos/Info.plist.template" > "$CONTENTS/Info.plist"

# Icon (best effort): convert the bundled .ico to .icns, falling back to a raw copy.
ICON_SRC="$REPO_ROOT/src/Nuvio.Desktop/Assets/avalonia-logo.ico"
if [ -f "$ICON_SRC" ]; then
  if command -v sips >/dev/null 2>&1 && sips -s format icns "$ICON_SRC" --out "$RES_DIR/nuvio.icns" >/dev/null 2>&1; then
    echo "    icon: generated nuvio.icns via sips"
  else
    cp "$ICON_SRC" "$RES_DIR/nuvio.icns"
    echo "    icon: sips unavailable; copied .ico as nuvio.icns (placeholder)"
  fi
fi

echo "==> Staging compliance files into the bundle"
bash "$REPO_ROOT/scripts/lib/stage-compliance.sh" "$RES_DIR" "$REPO_ROOT"

echo "==> Launch/exit smoke (--self-check)"
HOST_ARCH="$(uname -m 2>/dev/null || echo unknown)"
case "$HOST_ARCH" in
  arm64) HOST_RID="osx-arm64" ;;
  x86_64) HOST_RID="osx-x64" ;;
  *) HOST_RID="osx-unknown" ;;
esac
if [ "$(uname -s 2>/dev/null)" = "Darwin" ] && [ "$RID" = "$HOST_RID" ]; then
  "$MACOS_DIR/$EXECUTABLE" --self-check
else
  echo "    skipped: not running on matching macOS host ($RID vs $HOST_RID)"
fi

# ---- Signing + notarization (only when env vars are present) ----
sign_requested=true
for var in NUVIO_MACOS_DEV_ID NUVIO_MACOS_TEAM_ID NUVIO_MACOS_AC_PASSWORD; do
  if [ -z "${!var:-}" ]; then sign_requested=false; fi
done

mkdir -p "$DIST_DIR"

if [ "$sign_requested" = true ]; then
  echo "==> Signing app (Developer ID: $NUVIO_MACOS_DEV_ID)"
  codesign --force --deep --options runtime --timestamp \
    --entitlements "$REPO_ROOT/packaging/macos/entitlements.plist" \
    --sign "$NUVIO_MACOS_DEV_ID" "$APP_DIR"
  codesign --verify --deep --strict --verbose=2 "$APP_DIR"

  echo "==> Building DMG"
  rm -f "$DMG"
  hdiutil create -volname "Nuvio Desktop" -srcfolder "$APP_DIR" -ov -format UDZO "$DMG"

  echo "==> Notarizing DMG"
  if [ -n "${NUVIO_MACOS_AC_APPLE_ID:-}" ]; then
    xcrun notarytool submit "$DMG" \
      --apple-id "$NUVIO_MACOS_AC_APPLE_ID" \
      --team-id "$NUVIO_MACOS_TEAM_ID" \
      --password "$NUVIO_MACOS_AC_PASSWORD" \
      --wait
  else
    # Treat NUVIO_MACOS_AC_PASSWORD as a stored notarytool keychain profile name.
    xcrun notarytool submit "$DMG" \
      --keychain-profile "$NUVIO_MACOS_AC_PASSWORD" \
      --wait
  fi

  echo "==> Stapling notarization ticket"
  xcrun stapler staple "$APP_DIR"
  xcrun stapler staple "$DMG"
  echo "Signed + notarized: $APP_DIR and $DMG"
else
  echo "==> Signing skipped (NUVIO_MACOS_DEV_ID/TEAM_ID/AC_PASSWORD not all set); emitting UNSIGNED artifacts"
  echo "==> Building unsigned DMG"
  rm -f "$DMG"
  if command -v hdiutil >/dev/null 2>&1; then
    hdiutil create -volname "Nuvio Desktop" -srcfolder "$APP_DIR" -ov -format UDZO "$DMG"
    echo "Wrote (unsigned) $APP_DIR and $DMG"
  else
    echo "    hdiutil unavailable (non-macOS host); produced unsigned .app only: $APP_DIR" >&2
  fi
fi
