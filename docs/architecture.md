# Architecture

Nuvio Desktop is organized around portable app behavior and isolated platform seams.

## Layers

- `Nuvio.Desktop`: Avalonia UI, shell routing, view models, desktop interaction.
- `Nuvio.Core`: app models, validation, source/addon contracts, redaction helpers.
- `Nuvio.Player`: `IPlayerEngine` and mpv/libmpv playback implementations.
- `Nuvio.Platform`: OS detection, paths, native dependency loading, packaging facts.
- `Nuvio.Data`: SQLite stores, migrations, metadata cache, image cache.

## Phase 1 References

- `phase-1-behavior-mapping.md` records the NuvioMobile behavior mapped into Core models and parsers.
- `player-parity-notes.md` records NuvioTV playback UX notes for later phases.
- `adr/0001-rebuild-desktop-ui-in-avalonia.md` records why the desktop UI is rebuilt instead of directly ported from Android/mobile UI code.

## Rules

- UI code must not call mpv/libmpv directly.
- Platform-specific paths and native loading belong in `Nuvio.Platform`.
- Catalogs and poster grids must be paged, virtualized, and cancellable.
- Logs must redact stream URLs, query strings, tokens, and sensitive headers by default.
- Every milestone needs Windows, macOS, and Linux regression coverage.

## Local Storage

Phase 5 durable state is SQLite-backed in `Nuvio.Data` with hand-written migrations through `SqliteMigrationRunner`; EF Core is intentionally not used. `Nuvio.Platform` owns OS-specific app-data, cache, log, database, backup, and image-cache paths. `Nuvio.Desktop` wires live mode to SQLite-backed addons, settings, metadata, and watch-progress repositories, while fixture mode can keep in-memory stores.

Settings, addons, and watch progress are user data. Metadata rows and image files are cache data: metadata uses TTL expiry and images use disk LRU eviction. Clear-cache actions remove metadata, disk images, and decoded images only; they do not delete settings, addon registrations, or watch progress.

## Desktop UX (Phase 7)

The shell is themed entirely through design tokens, not hardcoded colors. `Styles/Theme.axaml` defines a Light and Dark `ThemeDictionaries` set of named brushes (`BackgroundBrush`, `SurfaceBrush`, `TextPrimaryBrush`, `AccentBrush`, `FocusRingBrush`, …); every view references them with `DynamicResource` so a variant swap is live. `IThemeController`/`ThemeController` maps the persisted `ThemeMode` to `Application.RequestedThemeVariant` (System → `ThemeVariant.Default`, which follows the OS). The theme is applied at startup before the window shows and again live as the Settings combo changes.

Reusable chrome lives in `Controls/`: `StateOverlay` (the single home for loading/empty/error states and actionable recovery copy), `PosterCard` (a `Button` subclass centralizing poster sizing, the focus ring, and the accessibility name), and `PlayerControls` (the player control bar, reused by the full and mini player). Their templates live in `Styles/Controls.axaml` and consume the same tokens.

`MainWindowViewModel` owns the responsive `DesktopLayoutMode` (Compact/Normal/Wide/Tv), recomputed from window width on `SizeChanged` (breakpoints at 1000 and 1400 DIP) with TV focus mode overriding to `Tv`. The mode drives sidebar collapse and poster-card dimensions. Fullscreen is promoted to the window level: the view model mirrors `PlayerViewModel.IsFullscreenIntent` into `IsPlayerFullscreen`, the window code-behind sets `WindowState.FullScreen` (restoring the prior state on exit), and chrome (menu, sidebar, top bar, footer) binds visibility to `IsChromeVisible`.

Platform seams: macOS gets a `NativeMenu` (built in `MainWindow.BuildNativeMenu`, guarded by `PlatformInfo.Family`); Windows/Linux keep the in-window `Menu` with accelerators. "Open media file…" uses `TopLevel.StorageProvider`; the picker stays in the view while `MainWindowViewModel.PlayLocalFileAsync` carries the testable action. High-DPI image decode sizing (`ImageDecodeSizing`) bounds poster/backdrop decode width by layout size × render scaling so large sources never fully decode.

## Home & Catalog Parity (Phase 11)

The Home pipeline mirrors NuvioMobile's `HomeRepository`. `CatalogService` emits **one rail per Home-eligible catalog** (a catalog whose only required extras are `skip`/`limit`), not one per addon: `BuildHomeCandidates` enumerates every enabled manifest's catalogs and dedupes by `manifestId:type:catalogId`. `HomeRailsAsync` fans out all candidates at once; `StreamHomeRailsAsync` is an `IAsyncEnumerable` that fetches in batches of `HomeRailDefaults.CatalogFetchBatchSize` (4) and yields each rail as its batch completes, so `HomePageViewModel` paints Home **incrementally** (progressive publish). Each rail is titled `"{catalog.Name} — {MediaTypeLabel(type)}"` and capped at `CatalogPreviewFetchLimit` (18). The Home-eligibility predicate (`CatalogService.IsHomeEligible`) is shared with `LiveCatalogDataSource` so Home and Catalog browse agree; the `skip`/`limit` exemption is a deliberate desktop relaxation of upstream's strict `.none { isRequired }` (recorded in `phase-1-behavior-mapping.md`).

`HomePageViewModel` composes the page: a **Continue Watching** rail first (from `IWatchProgressRepository.RecentAsync`, filtering effectively-completed entries), then the streamed catalog rails ordered/renamed/filtered per the persisted `HomeCatalogSettings`, and a **featured hero** (`HomeHeroViewModel`) built from a seeded, process-stable shuffle of the loaded rail items, distinct by `type:id`, capped at `HomeRailDefaults.HeroItemLimit` (8). When Home is empty it distinguishes "no catalog-capable addons" from "addons installed but all rails failed" via `ICatalogDataSource.HasCatalogCapableAddonsAsync` (`HomeEmptyState`). Live/fixture labelling binds `ICatalogDataSource.ModeLabel` — no hardcoded "Fixture" copy in live mode.

**Image-on-card loading.** Poster art loads through the same `IDesktopImageLoader`/`CachedImageLoader` + `ImageDecodeSizing` that power Details. `PosterCardViewModel.EnsureImageAsync` decodes the poster at a DPI-appropriate width and exposes `PosterImage`/`ShowInitial`; the `PosterCard` control triggers the load on `OnAttachedToVisualTree` and cancels it on `OnDetachedFromVisualTree`, giving load-on-realize and cancel-on-recycle for virtualized rows. On failure or with no URL the card falls back to the letter initial. `PosterCard` also carries a `Progress` overlay used by Continue Watching cards. The loader is threaded (nullable) through `PosterGridBuilder` into the Home/Search/Catalog grids; fixture mode without a loader is unchanged (initials only).

**Continue-watching enrichment.** The `watch_progress` table carries display metadata (`media_type`, `title`, `poster_url`, `background_url`) captured at playback time (`PlayerProgressRecorder`), so Continue Watching cards render without a render-time metadata lookup — mirroring upstream `WatchProgressEntry`. Migration v2 (`002_watch_progress_display_metadata`) adds the columns additively; legacy rows read back null and fall back to the title initial. Playback **resumes** from the stored position best-effort: `MainWindowViewModel.ResolveResumePositionAsync` looks up progress before play, and `PlayerViewModel` seeks once the file reports loaded.

**Catalog filters & Home customization.** The Catalog page surfaces per-catalog genre/type filters: `ICatalogDataSource.GetCatalogChoicesAsync` derives choices (and genre options) from enabled manifests, `SelectCatalog` picks the active catalog + genre, and the `genre` extra is threaded through `CatalogService.BrowseAsync`/`BuildCatalogUri`; changing a filter resets paging while keeping virtualization. Home customization (`HomeCustomizationViewModel`) lets the user reorder, enable/disable, rename, and toggle hero-sourcing per rail plus a global hero switch; it persists a `HomeCatalogSettings` snapshot via `ISettingsStore` under the `home_catalog` key (the same generic settings table, no new table) and reloads Home on save.

**Redirect policy.** `RedirectPolicy` follows validated cross-host HTTPS redirects (real addons like Cinemeta redirect to CDN/Cloudflare hosts) while still rejecting HTTPS→HTTP downgrades. This is safe because `NuvioHttpClient` already validates every redirect target with `AddonUrlPolicy.ValidateRemoteUri` (HTTPS-only, public host) and enforces `RedirectPolicy.MaxRedirects` before following.

## Performance Harness (Phase 9)

Performance is measured, not guessed. `PerfProbe` (`src/Nuvio.Desktop/PerfProbe.cs`) is a headless CLI mode wired into `Program.cs` next to `--self-check`: `--perf-probe` (or `NUVIO_PERF_PROBE=1`) boots the live service graph and runs scripted scenarios — cold-start, catalog-scroll, details-loop, and a gated playback-stop — reporting `elapsedMs` and managed-heap deltas as JSON (optionally to `--perf-out`). Because it is one binary, it produces a cross-platform baseline from the CI matrix without OS-specific scripts; `scripts/measure-perf.{ps1,sh}` wrap it and the old `measure-startup.ps1` forwards to it.

The probe owns *absolute* numbers (time, memory), which are **report-only** in CI. *Invariants* are asserted in the hard-failing xunit suites: `tests/Nuvio.Desktop.Tests/ViewModels/Phase9PerformanceTests.cs` proves poster-grid virtualization holds at 5,000 items (incl. scroll), the decoded/disk image caches respect their LRU caps, search cancellation storms resolve newest-wins with no stale overwrite, and player start/stop cycles dispose every engine without leaking. Gated real-engine teardown (no orphan mpv process; no obvious libmpv managed-heap growth) lives in the player integration tests. Budgets, the release gate, completed tuning decisions (9.8–9.9), and the still-open 9.7 real-window UI-thread profiling requirement are documented under `docs/perf/` and `docs/performance-budget.md`.
