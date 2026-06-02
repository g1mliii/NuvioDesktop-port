# Player Integration

Playback is owned by `Nuvio.Player` and exposed through `IPlayerEngine`.

## Milestones

1. External `mpv` process controlled through JSON IPC. ✅
2. External `mpv` discovery or bundled runtime per OS. ✅
3. Embedded `libmpv` through native bindings. 🚧 Phase 6 spike (external mpv remains the default and fallback).
4. Render API integration when the embedding path is proven. 🚧 Phase 6 spike (`OpenGlControlBase`).

## Discovery and Setup

`MpvProcessLocator` looks for real `mpv` binaries only. MPV Manager is allowed as an optional installer/configuration helper, but `mpv-manager` itself is never accepted as the playback executable and is not a runtime dependency.

Discovery order:

1. `NUVIO_MPV_PATH`
2. Nuvio-managed or bundled mpv path, when introduced
3. MPV Manager-installed mpv path, when discoverable
4. `PATH`
5. Common OS install locations

See `docs/mpv-setup.md` for user-facing setup guidance.

## External mpv Engine

`ExternalMpvEngine` starts `mpv` as a child process, connects through JSON IPC, observes playback state, and exposes everything through `IPlayerEngine`. The desktop UI must bind to player events and diagnostics instead of calling mpv directly.

Supported Phase 2 commands:

- Load source
- Play and pause
- Seek
- Stop
- Set volume
- Set fullscreen
- Select audio track
- Select subtitle track

Observed Phase 2 events:

- Availability and version
- File loaded and end-file
- Pause/play state
- Position and duration
- Buffer/cache state
- Audio/subtitle/video tracks
- Redacted mpv log and error messages

## Defaults

```text
hwdec=auto-safe
gpu-api=auto
vo=gpu-next
cache=yes
demuxer-max-bytes=150M
demuxer-max-back-bytes=50M
save-position-on-quit=no
config=no
```

Only one player instance should be active. Progress writes must be debounced during playback and flushed on pause, stop, and exit.

## Embedded libmpv (Phase 6 spike)

`LibMpvEngine` (`Nuvio.Player/LibMpv/`) is a second `IPlayerEngine` implementation that drives **libmpv** in-process through P/Invoke (`LibMpvNative`), instead of launching a child process. It emits the same `PlayerEvent` records as `ExternalMpvEngine`, so the desktop view-model is engine-agnostic.

This is a spike with fallback, not a cutover:

- **External mpv stays the default and the guaranteed fallback.** `SelectingPlayerEngineFactory` chooses libmpv only when the user selects "Embedded libmpv (experimental)" in Settings **and** the native library loads; otherwise it returns `ExternalMpvEngine`. The player view shows which engine is active.
- **Runtime failover** (parity with upstream's startup engine failover): if libmpv is selected but fails to start at playback time, `PlayerViewModel` disposes the embedded engine and retries the same source on external mpv, keeping the same playback session and progress recorder.
- **No libmpv binaries are bundled.** Bundling/provenance is deferred to packaging (Phase 8).

### Native library discovery

`LibMpvLibraryLocator` (`Nuvio.Platform`) mirrors `MpvProcessLocator` but targets the shared library. `LibMpvNativeLibraryLoader` (`Nuvio.Platform`) performs the actual native handle load; `LibMpvLibraryLoader` (`Nuvio.Player`) only binds that handle to libmpv P/Invoke through `NativeLibrary.SetDllImportResolver`. Platform is therefore the source of truth for native loading, because a file existing on disk does not guarantee a working load.

Discovery order:

1. `NUVIO_LIBMPV_PATH` (explicit file path).
2. Nuvio-managed paths next to the app and under `runtimes/<rid>/native`.
3. Common per-OS system locations.
4. Bare library names resolved by the OS loader search path.

Per-OS library names (most preferred first):

- Windows: `libmpv-2.dll`, `mpv-2.dll`, `libmpv.dll`, `mpv-1.dll`
- macOS: `libmpv.2.dylib`, `libmpv.dylib`
- Linux: `libmpv.so.2`, `libmpv.so.1`, `libmpv.so`

### Supported ABI / version target

Targets the stable libmpv **client API** with the OpenGL render API (`MPV_RENDER_API_TYPE_OPENGL`), i.e. client API ≥ 1.101 (mpv ≥ 0.29). The loaded `mpv_client_api_version` is reported in diagnostics. This target must be pinned before any binaries are bundled in Phase 8.

### Render integration

`LibMpvVideoView` (`OpenGlControlBase`) binds to the optional `IPlayerRenderSource` contract exposed by embedded-capable engines, creates an `mpv_render_context` via `LibMpvRenderSession`, feeds mpv a GL proc-address resolver from Avalonia's GL context, and renders into the control's framebuffer. The engine owns the render session and frees the render context before `mpv_terminate_destroy` (required by the render API). Render-context creation failure degrades gracefully: audio/events continue and external mpv remains available.

### Known limitations

- Render path is a spike; on-screen video correctness (HiDPI sizing, flip orientation, color) is validated case by case per OS and not yet a hard gate.
- `http-header-fields` is set best-effort via a comma-joined property; header values containing commas are not escaped.

## Fullscreen and window UX (Phase 7)

Player fullscreen is a window-level concern, not an engine property. `PlayerViewModel.IsFullscreenIntent` is mirrored into `MainWindowViewModel.IsPlayerFullscreen`; the `MainWindow` code-behind drives `WindowState.FullScreen` and restores the pre-fullscreen state on exit, while chrome (menu, sidebar, top bar, footer) hides via `IsChromeVisible`. Toggle with the Fullscreen control or **F11**; **Esc** exits fullscreen before returning to browsing. The engine's `SetFullscreenAsync` is still called so external mpv stays in sync, but the authoritative state is the Avalonia window.

Per-OS notes:

- **macOS** uses native full-screen spaces; `WindowState.FullScreen` is the correct Avalonia abstraction and animates into a dedicated space. The native menu bar (App/File/View) is used instead of the in-window menu.
- **Windows/Linux** enter borderless full-screen on the current monitor; the in-window menu is hidden while fullscreen and shown again on exit.

The compact **mini-player** (`PlayerViewModel.IsMiniMode`) repositions the same video surface into a docked corner — it is a layout change only, so the single active engine instance keeps playing (the "never two players" invariant holds).

### High-DPI image decoding

Posters and backdrops are decoded to a bounded pixel width via `ImageDecodeSizing` (layout size × render scaling, clamped to hard ceilings) so a 4K source never decodes at full resolution per tile. The decode width is part of the decoded-image cache key, so the small poster decode and the large backdrop decode of the same URL never collide.

## Regression

The normal verification scripts run all non-mpv tests. Set `NUVIO_RUN_MPV_INTEGRATION=1` when a real `mpv` binary is installed to run JSON IPC and generated-fixture playback smoke tests. CI installs mpv on Windows, macOS, and Linux and runs the integration path.

For embedded libmpv, set `NUVIO_RUN_LIBMPV_INTEGRATION=1` on a host with libmpv installed to run the gated `LibMpvEngine` init/load/play/seek/teardown and repeated create/dispose smokes. Without the flag these tests no-op so the normal suite stays green without native dependencies. The `LibMpvLibraryLocator` filename/discovery tests and headless render-host contract tests run on any host.
