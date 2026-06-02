# Regression Checklist

## Every Phase

- Windows build/test passes.
- macOS build/test passes.
- Linux build/test passes.
- Docs match the implemented behavior.
- Compliance files remain present.

## Phase 0 Gate

- Empty Avalonia shell builds.
- CI matrix covers Windows, macOS, and Ubuntu.
- Runtime target matrix is documented.
- Compliance test fails if `LICENSE` or `NOTICE` is missing.
- No playback implementation has started before the foundation is stable.

## Phase 1 Gate

- Core fixture tests cover valid/invalid addon manifests, stream source mapping, subtitle mapping, metadata/catalog parsing, log redaction, and watch progress normalization.
- Windows local gate: `./scripts/verify-all.ps1`.
- macOS/Linux gate: `.github/workflows/ci.yml` matrix or `./scripts/verify-all.sh` on each Unix target.
- No Avalonia UI, mpv runtime, live addon fetch, or platform-native loading is introduced by Phase 1.
- Logs redact query strings and sensitive playback headers while preserving non-sensitive playback headers for `IPlayerEngine` handoff.

## Phase 2 Gate

- `MpvProcessLocator` discovers direct, Nuvio-managed, MPV Manager-installed, `PATH`, Chocolatey-installed Windows, and common-location `mpv` binaries without accepting `mpv-manager` as a player.
- `ExternalMpvEngine` starts mpv as a child process and controls it through JSON IPC.
- JSON IPC command serialization, response parsing, event mapping, timeout handling, startup connection retry, and process-exit error handling are covered by tests or opt-in integration.
- Load, play, pause, seek, stop, volume, fullscreen, audio track selection, and subtitle track selection route through `IPlayerEngine`.
- Observed pause, position, duration, file loaded, end-file, buffering/cache, and track-list state becomes `PlayerEvent` data.
- Avalonia diagnostics show mpv found/not found, path, version, source, and setup policy.
- MPV Manager remains an optional setup helper and is not launched or bundled by Nuvio.
- Nuvio-owned external mpv defaults include `vo=gpu-next`, `hwdec=auto-safe`, controlled cache settings, and `config=no`.
- Player log redaction covers stream URLs, query strings, and sensitive headers before IPC command logs or load failures are surfaced.
- Generated local media is used for opt-in playback tests; real private stream URLs are not committed.
- Opt-in real mpv IPC and generated-fixture playback smokes pass with `NUVIO_RUN_MPV_INTEGRATION=1`.
- Windows local gate: `./scripts/verify-all.ps1`.
- macOS/Linux gate: `.github/workflows/ci.yml` matrix or `./scripts/verify-all.sh` on each Unix target.
- CI installs mpv on Windows, macOS, and Linux and runs the opt-in integration path.

## Phase 3 Gate

- Fixture desktop shell renders Home, Search, Details, and Player routes without real network calls.
- Home, Search, Catalog, Details, Addons, Settings, and Player route transitions are deterministic.
- Search requests debounce user input and cancel stale work.
- Details stream play actions route through `IPlayerEngine`; UI code does not call mpv directly.
- Player controls and shortcuts cover play/pause, seek backward/forward, stop, fullscreen intent, and return to browsing.
- Search shortcut conventions use Ctrl+F on Windows/Linux and Cmd+F on macOS.
- Catalog poster rows use an explicit `VirtualizingStackPanel`; the 1,000-item fixture smoke must not realize all poster cards.
- Poster and backdrop surfaces have fixed dimensions to avoid resize churn and bitmap over-allocation.
- Loading, empty, and error states render for shell data routes.
- Headless Avalonia UI/component tests run before any OS GUI automation is required.
- Windows local gate: `./scripts/verify-all.ps1`.
- macOS/Linux gate: `.github/workflows/ci.yml` matrix or `./scripts/verify-all.sh` on each Unix target.

## Phase 5 Gate

- SQLite startup creates app-data/cache/log/backup/image-cache directories through `Nuvio.Platform` paths on Windows, macOS, and Linux.
- Hand-written migrations create `schema_migrations`, `settings`, `addons`, `watch_progress`, `metadata_cache`, and `image_cache_entries`.
- Existing databases are backed up before migration; failed migrations restore the backup; corrupt databases are moved aside before a clean database is created.
- Settings, addons, metadata cache, and watch progress persist across app restarts in live mode.
- Watch progress is keyed by media/episode, ignores sub-1-second positions, normalizes percent/duration, and is retained until explicitly cleared.
- Player progress writes are debounced during playback and flushed on pause, stop, errors, playback end, return, dispose, and playback replacement.
- Metadata/images are cache data: metadata expires by TTL, disk images evict by LRU under the configured byte cap, and decoded images respect the memory item cap.
- Settings clear-cache removes metadata, disk image entries/files, and decoded images while preserving settings, addons, and watch progress.
- Diagnostic logs remain session-only in Phase 5; no persistent log retention policy is introduced yet.
- Windows local gate: `./scripts/verify-all.ps1`.
- macOS/Linux gate: `.github/workflows/ci.yml` matrix or `./scripts/verify-all.sh` on each Unix target.

## Phase 6 Gate

- `LibMpvLibraryLocator` selects the correct shared-library filename per OS (`libmpv-2.dll`, `libmpv.2.dylib`, `libmpv.so.2`) and honors the `NUVIO_LIBMPV_PATH` override; `LibMpvNativeLibraryLoader` performs native loading in `Nuvio.Platform`; a missing library yields a clear diagnostic — runnable on any host.
- `LibMpvEngine` implements `IPlayerEngine` and emits the same high-level `PlayerEvent` records as `ExternalMpvEngine` (availability, file loaded, position/duration, state, buffering, track list, end-file, redacted log/error).
- `SelectingPlayerEngineFactory` returns embedded libmpv only when requested and available, and falls back to external mpv otherwise so embedded failures never strand the user; the player surfaces which engine is active.
- External mpv remains the default engine and the guaranteed fallback; no libmpv binaries are bundled (deferred to Phase 8).
- The render spike (`LibMpvVideoView` + `IPlayerRenderSource` + `LibMpvRenderSession`) attaches the OpenGL render context only for embedded render sources, hides placeholder content while embedded video is active, and degrades gracefully on creation failure; the render context is freed before `mpv_terminate_destroy`.
- Opt-in embedded smokes (libmpv init/load/play/seek/teardown and repeated create/dispose) pass with `NUVIO_RUN_LIBMPV_INTEGRATION=1` on a host with libmpv installed.
- Per-OS libmpv limitations and the supported client API/ABI target are documented in `docs/player-integration.md`.
- Windows local gate: `./scripts/verify-all.ps1`.
- macOS/Linux gate: `.github/workflows/ci.yml` matrix or `./scripts/verify-all.sh` on each Unix target.

## Phase 7 Gate

- Theme tokens live in `Styles/Theme.axaml` (Light/Dark `ThemeDictionaries`); every view consumes them via `DynamicResource`, and the Theme setting re-themes the whole chrome live (`IThemeController` → `Application.RequestedThemeVariant`). System mode resolves to `ThemeVariant.Default` and follows the OS.
- Shared controls `StateOverlay` (loading/empty/error + actionable recovery copy), `PosterCard`, and `PlayerControls` replace the previously copy-pasted inline blocks; poster cards keep `x:Name="PosterCard"` and stay virtualized.
- Responsive `DesktopLayoutMode` (Compact/Normal/Wide/Tv) is driven by window width (breakpoints 1000/1400 DIP); the nav rail collapses to an icon strip in Compact, and poster cards size from the active mode. A resize-stress pass keeps virtualization bounded with no exceptions.
- Every interactive control is keyboard reachable with a visible focus ring (shared `FocusAdorner` style); the search → select → play → pause → seek → exit workflow is keyboard-drivable, including F11 fullscreen.
- Player fullscreen is promoted to the window level (`MainWindow.WindowState = FullScreen`), hides chrome (menu, sidebar, top bar, footer), and toggles via F11; Esc exits fullscreen before returning. The pre-fullscreen window state is restored on exit.
- macOS uses a `NativeMenu` (App / File / View) guarded by `PlatformInfo.Family`; Windows/Linux keep the in-window `Menu` with accelerators. The in-window menu is hidden on macOS and while fullscreen.
- "Open media file…" uses `StorageProvider.OpenFilePickerAsync`; selection plays through the same `IPlayerEngine`. Dialog invocation stays in the view; `PlayLocalFileAsync` is testable directly.
- TV focus mode (`DesktopSettings.TvFocusMode`) forces the Tv layout / larger focus targets and round-trips through `SqliteSettingsStore`.
- Mini-player (`PlayerViewModel.IsMiniMode`) reuses `PlayerControls` and keeps the single active engine instance alive.
- `AutomationProperties.Name` is set on nav buttons, back/search, player controls, and settings inputs.
- High-DPI: posters/backdrops decode to a bounded pixel width (`ImageDecodeSizing`, keyed off layout size × render scaling) so a 4K source never fully decodes; decode width is part of the decoded-cache key.
- Windows local gate: `./scripts/verify-all.ps1`.
- macOS/Linux gate: `.github/workflows/ci.yml` matrix or `./scripts/verify-all.sh` on each Unix target.

## Phase 8 Gate

- Versioning is centralized in `Directory.Build.props` (`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`, default `0.8.0`) and overridable with `-p:Version=`; artifact file names and the `--self-check` version come from it.
- mpv/libmpv is **not** bundled; the app relies on the existing `Nuvio.Platform` discovery chain (`NUVIO_MPV_PATH`/`NUVIO_LIBMPV_PATH` → app-managed `./mpv/` & `runtimes/<rid>/native` → system → OS loader), documented in `docs/native-dependency-provenance.md`.
- Each `scripts/package-*` publishes self-contained per RID, stages the compliance files (`LICENSE`, `NOTICE`, `dependency-licenses.md`, `source-availability.md`, `native-dependency-provenance.md`), runs the published binary with `--self-check`, and emits a labelled artifact in `artifacts/dist/`.
- Windows produces a portable `.zip` (unsigned); Linux produces a `.tar.gz`; macOS assembles a `Nuvio Desktop.app` (`Contents/MacOS` + `Resources` icon + generated `Info.plist`/`entitlements.plist`) and a DMG.
- macOS signing/notarization (`codesign --options runtime` → DMG → `notarytool submit --wait` → `stapler staple`) runs locally when `NUVIO_MACOS_DEV_ID`/`NUVIO_MACOS_TEAM_ID`/`NUVIO_MACOS_AC_PASSWORD` are set; CI always emits the **unsigned** `.app`/DMG.
- `--self-check` (in `Program.cs`, modeled on `--fixture-data`) boots the service graph headlessly, reports platform + app version + mpv/libmpv discovery, and exits 0; a missing external mpv is reported but does not fail it.
- `docs/dependency-licenses.md` is generated by `scripts/generate-license-inventory.*`, contains **no** `TBD`, keeps the curated external mpv/MPV-Manager rows, and the generator fails if a new package enters the shipped graph without a curated license.
- `scripts/make-source-bundle.*` produces a reproducible `artifacts/source/nuvio-desktop-src-<version>.tar.gz` via `git archive` that excludes the git-ignored `upstream/` tree.
- Artifact-contents test is opt-in via `NUVIO_ARTIFACT_DIR` (asserts the five compliance files in the artifact root; no-op when unset so the default suite stays green); `ComplianceFilesTests` requires the two new docs.
- CI: `package.yml` runs the per-OS packagers + `--self-check` and uploads the labelled artifact (macOS unsigned); `release.yml` regenerates the inventory (no-drift, no `TBD`), builds the source bundle, and runs the artifact-contents gate.
- Deferred to the post-MVP backlog: Windows MSIX/MSI (8.4) + Authenticode, Linux AppImage (8.8) + deb/rpm (8.9), and assembly trimming/single-file (Phase 9 size investigation).
- Windows local gate: `./scripts/verify-all.ps1`.
- macOS/Linux gate: `.github/workflows/ci.yml` matrix or `./scripts/verify-all.sh` on each Unix target.
