# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Nuvio Desktop is a cross-platform (Windows/macOS/Linux) desktop port of the Nuvio mobile/TV apps, built on **Avalonia + C#/.NET 10**, SQLite for local state, and **mpv/libmpv** for playback. It is **not** an Electron or browser-shell app — that is a hard constraint.

Work follows `nuvio_desktop_cross_platform_implementation_plan.md` in phases (currently through Phase 5 storage/cache, with a Phase 6 embedded-libmpv spike in progress). Do not mark a phase complete without updating the relevant `docs/` files and `docs/regression-checklist.md`.

## Commands

The repo standardizes on the verify scripts, which restore, build Release, run all tests, then assert required compliance/handoff docs exist. **Run before handing off work.**

```powershell
./scripts/verify-all.ps1   # Windows
```
```bash
./scripts/verify-all.sh    # macOS/Linux
```

Day-to-day:

```powershell
dotnet build NuvioDesktop.sln -c Release
dotnet test  NuvioDesktop.sln -c Release
dotnet run --project src/Nuvio.Desktop/Nuvio.Desktop.csproj

# Single test project / single test
dotnet test tests/Nuvio.Core.Tests/Nuvio.Core.Tests.csproj
dotnet test --filter "FullyQualifiedName~SomeTestClassOrMethod"

# Run the app against canned fixtures instead of live SQLite/network
dotnet run --project src/Nuvio.Desktop/Nuvio.Desktop.csproj -- --fixture-data
```

mpv setup for playback work lives in `docs/mpv-setup.md`. First-run bootstrap: `./scripts/bootstrap.ps1` (or `.sh`), then `./scripts/fetch-upstream.ps1`.

### Environment flags

- `NUVIO_FIXTURE_DATA=1` — same as `--fixture-data`; in-memory stores, no SQLite/network.
- `NUVIO_MPV_PATH` / `NUVIO_LIBMPV_PATH` — explicit binary/library path, highest priority in discovery.
- `NUVIO_RUN_MPV_INTEGRATION=1` — enables external-mpv JSON-IPC playback smoke tests (need a real `mpv` installed). CI sets this on all three OSes.
- `NUVIO_RUN_LIBMPV_INTEGRATION=1` — enables gated `LibMpvEngine` integration tests (need libmpv installed). Without either flag those tests no-op so the default suite stays green with no native deps.

## Architecture

Five projects under `src/`, mirrored by test projects under `tests/`. Dependencies flow downward; the layering is enforced by intent, not just project references:

- **`Nuvio.Core`** — portable domain: addon/stream/metadata/progress/settings models, validation, services (`AddonService`, `CatalogService`, `MetadataService`, `StreamResolver`, `SubtitleService`), HTTP plumbing (`NuvioHttpClient`, `PerHostThrottler`), and **redaction** (`Security/`). No UI, no platform, no playback.
- **`Nuvio.Player`** — playback behind a single `IPlayerEngine` interface. Two implementations: `ExternalMpv/ExternalMpvEngine` (child `mpv` process over JSON IPC — the **default and guaranteed fallback**) and `LibMpv/LibMpvEngine` (in-process libmpv via P/Invoke — experimental spike). Both emit identical `PlayerEvent` records so consumers are engine-agnostic.
- **`Nuvio.Platform`** — the only place for OS-specific code: `PlatformInfoProvider`, `PlatformPaths` (app-data/cache/log/db/image paths), `MpvProcessLocator`, and native library loading (`LibMpvLibraryLocator`, `LibMpvNativeLibraryLoader`). Platform is the source of truth for native loading because a file existing on disk does not guarantee a working load.
- **`Nuvio.Data`** — SQLite persistence (`Sqlite/`) with **hand-written migrations via `SqliteMigrationRunner` — EF Core is intentionally not used**. Holds addon/settings/watch-progress repositories, `SqliteMetadataCache`, and `Images/` (disk LRU image cache).
- **`Nuvio.Desktop`** — Avalonia 12 MVVM shell (CommunityToolkit.Mvvm, compiled bindings by default). `Views/` + `ViewModels/` + `Services/`. Wiring lives in `Services/DesktopBootstrap.cs`.

### Key wiring rules

- **UI must never call mpv/libmpv directly.** Bind to `IPlayerEngine` events/diagnostics. Engine selection is `SelectingPlayerEngineFactory`: libmpv only when the user opts in **and** the native lib loads; otherwise external mpv. `PlayerViewModel` also does runtime failover — if libmpv fails at playback time it disposes and retries the source on external mpv within the same session.
- **Live vs fixture is a hard fork at startup** (`App.axaml.cs`): `DesktopBootstrap.BuildLive(...)` constructs the full SQLite-backed graph (`DesktopServiceHost`); `MainWindowViewModel.CreateFixture()` uses in-memory `Fixture*` services. Most service interfaces (`ICatalogDataSource`, `ISettingsStore`, `IDesktopImageLoader`, etc.) have a `Live*`/`Fixture*` pair.
- **Catalogs and poster grids must be paged, virtualized, and cancellable** — never load full catalogs or unbounded poster images into memory.
- **Logs must redact** stream URLs, query strings, tokens, and sensitive headers by default (use `Nuvio.Core/Security` redaction helpers).
- **Settings/addons/watch-progress are user data; metadata rows and images are cache data.** Clear-cache (`DesktopCacheMaintenanceService`) removes metadata + disk/decoded images only — never settings, addon registrations, or progress.

## Conventions

- TFM `net10.0` (SDK pinned to `10.0.200` in `global.json`), nullable + implicit usings on, shared via `Directory.Build.props`. Warnings are not errors.
- Tests are **xunit**; `Avalonia.Headless` drives UI smoke tests. Production projects expose internals to their test project via `InternalsVisibleTo`.
- `upstream/NuvioMobile` and `upstream/NuvioTV` are git-ignored reference checkouts. Port behavior deliberately into C# with tests and behavior-mapping docs (`docs/phase-1-behavior-mapping.md`, `docs/player-parity-notes.md`); do not commit upstream code.
- This is GPL-adjacent (mpv): keep compliance files current (`LICENSE`, `NOTICE`, `docs/legal-compliance.md`, `docs/dependency-licenses.md`) — `verify-all` and CI fail if they go missing.
