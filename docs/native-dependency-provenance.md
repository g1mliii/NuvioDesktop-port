# Native Dependency Provenance (mpv / libmpv)

## Policy: mpv/libmpv is NOT bundled

Nuvio Desktop ships the **.NET application only**. It does **not** bundle, redistribute,
or download any `mpv` executable or `libmpv` shared library. Playback relies on an mpv
that is already present on the user's machine (or dropped into an app-managed folder),
discovered at runtime. This keeps release artifacts free of mpv's own (LGPL/GPL-mixed)
binary distribution and licensing obligations.

Because nothing is bundled, the per-binary provenance table below is currently **empty
by design**. If a binary is ever shipped inside an artifact, it must be recorded here
before release (see "If a binary is ever dropped in").

## Discovery / drop-in convention (already implemented)

Discovery order is implemented in `src/Nuvio.Platform` and is **not** changed by
packaging — Phase 8 only documents and stages around it.

**External mpv executable** (`MpvProcessLocator`):

1. `NUVIO_MPV_PATH` environment override (highest priority).
2. App-managed, next to the executable:
   - `./mpv/<mpv|mpv.exe>`
   - `./tools/mpv/<mpv|mpv.exe>`
   - `./runtimes/<win|osx|linux>/native/<mpv|mpv.exe>`
3. MPV Manager-installed locations (optional setup helper only).
4. `PATH`.
5. Common per-OS install locations.

**In-process libmpv** (`LibMpvLibraryLocator`, experimental engine):

1. `NUVIO_LIBMPV_PATH` environment override.
2. App-managed, next to the executable, including `./runtimes/<rid>/native/<libmpv>`.
3. Common per-OS system library locations.
4. Bare library names left to the OS loader.

Per-OS library file names selected by the locator:

| OS | libmpv names (most preferred first) |
|---|---|
| Windows | `libmpv-2.dll`, `mpv-2.dll`, `libmpv.dll`, `mpv-1.dll` |
| macOS | `libmpv.2.dylib`, `libmpv.dylib` |
| Linux | `libmpv.so.2`, `libmpv.so.1`, `libmpv.so` |

> Note: a file existing on disk does not guarantee a working load. `Nuvio.Platform`
> remains the source of truth for native loading (`LibMpvNativeLibraryLoader`); the
> external-mpv engine is the guaranteed fallback.

## Supported version / ABI

- **External mpv:** any reasonably recent `mpv` that speaks JSON IPC (mpv 0.33+
  recommended). Nuvio launches the binary and controls it over the IPC socket/pipe.
- **libmpv (embedded, experimental):** targets the mpv **client API ABI 2**
  (`MPV_CLIENT_API_VERSION` major 2), i.e. `libmpv.so.2` / `libmpv.2.dylib` /
  `libmpv-2.dll`. ABI 1 names are accepted as a fallback but not guaranteed.

## Per-OS install / drop-in guidance for users

| OS | Easiest install | App-managed drop-in |
|---|---|---|
| Windows | `mpv` on `PATH`, `C:\Program Files\mpv\mpv.exe`, or Chocolatey/Scoop | put `mpv.exe` in `mpv\` next to `Nuvio.Desktop.exe`, or set `NUVIO_MPV_PATH` |
| macOS | `brew install mpv` (`/opt/homebrew/bin/mpv` or `/usr/local/bin/mpv`) | put `mpv` in `Nuvio Desktop.app/Contents/MacOS/mpv/`, or set `NUVIO_MPV_PATH` |
| Linux | distro package: `apt install mpv` / `dnf install mpv` | put `mpv` in `mpv/` next to the `Nuvio.Desktop` binary, or set `NUVIO_MPV_PATH` |

See `docs/mpv-setup.md` for the full setup walkthrough and the MPV Manager note.

## If a binary is ever dropped in

If a future release bundles an mpv/libmpv binary (a deliberate, separately reviewed
decision), record provenance **before** shipping by adding a row here:

| Artifact / RID | File | mpv/libmpv version | Source URL | License | SHA-256 | Date | Reviewed by |
|---|---|---|---|---|---|---|---|
| _(none bundled)_ | — | — | — | — | — | — | — |

A bundled binary must also be added to `dependency-licenses.md` (with `Bundled = yes`),
covered by the GPL/LGPL source-availability obligations in `source-availability.md`, and
its native-loading path verified through `Nuvio.Platform`.
