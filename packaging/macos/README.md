# macOS packaging

`scripts/package-macos.sh` publishes the app, assembles a `Nuvio Desktop.app` bundle, and
produces a DMG.

- `Info.plist.template` — rendered into `Contents/Info.plist` with the release version.
- `entitlements.plist` — hardened-runtime entitlements applied at `codesign` time.

## Bundle layout

```
Nuvio Desktop.app/
  Contents/
    Info.plist
    MacOS/Nuvio.Desktop        (+ all published files)
    Resources/nuvio.icns        (icon)
    Resources/LICENSE, NOTICE, dependency-licenses.md,
              source-availability.md, native-dependency-provenance.md
```

## Signing + notarization (local, on the user's Mac)

CI always produces an **unsigned** `.app` + DMG (no signing secrets in CI). To sign and
notarize, run the script locally with these environment variables set:

| Variable | Meaning |
|---|---|
| `NUVIO_MACOS_DEV_ID` | "Developer ID Application: Name (TEAMID)" identity in the keychain |
| `NUVIO_MACOS_TEAM_ID` | Apple Developer Team ID |
| `NUVIO_MACOS_AC_PASSWORD` | App-Store-Connect app-specific password (or a `notarytool` keychain profile value) |
| `NUVIO_MACOS_AC_APPLE_ID` | (optional) Apple ID email for notarytool; required if not using a stored profile |

When all required variables are present the script runs `codesign --deep --options
runtime`, builds the DMG, submits it with `xcrun notarytool submit --wait`, and staples
the ticket. When they are absent it logs that signing was skipped and emits the unsigned
artifacts. See `docs/packaging.md`.
