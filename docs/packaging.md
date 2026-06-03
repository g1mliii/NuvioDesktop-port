# Packaging

Phase 8 turns the raw per-RID `dotnet publish` into labelled, compliance-complete,
launch-smoked artifacts per OS, plus a GPL source tarball and a generated license
inventory. This document is the strategy; the scripts under `scripts/` are the
implementation.

## Pipeline (per OS)

Every packager follows the same shape:

```
dotnet publish (self-contained, per RID, -p:Version=...)
  → stage compliance files into the artifact
  → run the published binary with --self-check (launch/exit smoke)
  → produce a labelled artifact in artifacts/dist/
```

| OS | Script | Artifact | Signing |
|---|---|---|---|
| Windows | `scripts/package-windows.ps1` | `nuvio-desktop-<version>-win-x64.zip` | unsigned (Authenticode deferred) |
| macOS | `scripts/package-macos.sh` | `Nuvio Desktop.app` + `nuvio-desktop-<version>-osx-arm64.dmg` | signed + notarized when env vars present; otherwise unsigned |
| Linux | `scripts/package-linux.sh` | `nuvio-desktop-<version>-linux-x64.tar.gz` | n/a |

Each script takes an optional RID and version, e.g. `./scripts/package-windows.ps1
-Rid win-arm64` or `./scripts/package-linux.sh linux-arm64`. The version defaults to
`<VersionPrefix>` in `Directory.Build.props` (currently `0.8.0`) and is overridable.

### Self-contained publish

Builds are `--self-contained true` per RID, so the .NET runtime and the
Avalonia/Skia/SQLite native assets for the target RID ship inside the artifact. No
trimming or single-file packaging is applied in Phase 8 — binary-size investigation
(trimming / single-file / R2R) is a **Phase 9** task.

## Versioning

`Directory.Build.props` sets `VersionPrefix`/`Version`, `AssemblyVersion`, `FileVersion`,
and `InformationalVersion`. Override at publish time with `-p:Version=0.8.1`. The
informational version is what `--self-check` and the GPL written offer reference, and it
is encoded in artifact file names.

## Launch/exit smoke: `--self-check`

`Nuvio.Desktop --self-check` (handled in `Program.cs` before Avalonia starts, modeled on
`--fixture-data`) boots the live service graph headlessly, prints the app version,
platform, and mpv/libmpv discovery result, and exits 0. Packagers and `package.yml` run
it as the smoke; a missing external mpv is reported but does not fail the check (mpv is an
optional, externally-installed dependency). It is only executed when the published RID
matches the host OS/arch (cross-RID publishes skip it).

## Compliance staging

`scripts/lib/stage-compliance.{sh,ps1}` copies the release-required files into the
artifact (and, on macOS, into `Contents/Resources`):

- `LICENSE`
- `NOTICE`
- `docs/dependency-licenses.md`
- `docs/source-availability.md`
- `docs/native-dependency-provenance.md`

The same set is copied to the build output by the `<None Include>` block in
`Nuvio.Desktop.csproj`, asserted by the artifact-contents tests, and gated in
`release.yml`.

## License inventory

`scripts/generate-license-inventory.{sh,ps1}` regenerates `docs/dependency-licenses.md`
from a curated license map and fails if a new package enters the shipped dependency
graph without a curated license (so the inventory never contains an unresolved
placeholder). The curated external rows for mpv/libmpv and MPV Manager are preserved.

## Source bundle (GPL)

`scripts/make-source-bundle.{sh,ps1}` runs `git archive` from `HEAD` into
`artifacts/source/nuvio-desktop-src-<version>.tar.gz`. Because it uses `git archive`, it
honors `.gitignore` and excludes the `upstream/` reference checkouts. The written offer
and tarball satisfy the GPL source-availability obligation — see
[`source-availability.md`](source-availability.md).

## macOS signing + notarization

CI always produces an **unsigned** `.app` + DMG (no signing secrets in CI). To sign and
notarize, run `scripts/package-macos.sh` locally on a Mac with a Developer ID and these
env vars set:

| Variable | Meaning |
|---|---|
| `NUVIO_MACOS_DEV_ID` | "Developer ID Application: Name (TEAMID)" |
| `NUVIO_MACOS_TEAM_ID` | Apple Developer Team ID |
| `NUVIO_MACOS_AC_PASSWORD` | app-specific password, or a notarytool keychain profile name |
| `NUVIO_MACOS_AC_APPLE_ID` | (optional) Apple ID; selects id/team/password auth vs. keychain profile |

When present, the script runs `codesign --deep --options runtime` with
`packaging/macos/entitlements.plist`, builds the DMG, `xcrun notarytool submit --wait`,
and `xcrun stapler staple`. When absent, it logs that signing was skipped and emits the
unsigned artifacts. The hardened-runtime entitlements allow the .NET JIT and
`disable-library-validation` so a user-installed mpv/libmpv can load.

## Native dependency policy

mpv/libmpv is **not** bundled. The app discovers an external mpv / drops-in via the chain
documented in [`native-dependency-provenance.md`](native-dependency-provenance.md), which
is implemented in `Nuvio.Platform` (`MpvProcessLocator`, `LibMpvLibraryLocator`) and only
documented/staged-around here.

## Deferred to post-MVP backlog

These are explicitly **not** in Phase 8:

- Windows MSIX / MSI installers (plan 8.4) and Authenticode signing.
- Linux AppImage (plan 8.8) and deb/rpm packages (plan 8.9).
- Assembly trimming / single-file / size optimization (Phase 9 investigation).
