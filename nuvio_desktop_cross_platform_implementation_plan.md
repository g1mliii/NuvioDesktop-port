# Nuvio Desktop Cross-Platform Implementation Plan

## Context

Nuvio Desktop is a cross-platform desktop fork/port of Nuvio for **Windows, macOS, and Linux**. The goal is not to make a Windows-only build first and generalize later. The goal is one desktop architecture with all three desktop operating systems treated as first-class targets from the start.

The recommended base is **NuvioMobile**, because it already contains the most reusable cross-platform app logic, shared feature structure, repository patterns, and app behavior. **NuvioTV** should be used as a reference for TV-style browsing, playback UX, remote/focus behavior, and big-screen feature expectations, but it should not be the main fork base because its Android TV stack is tightly tied to Android SDK, Jetpack Compose TV, Media3/ExoPlayer, Hilt, Android lifecycle, and Gradle Android packaging.

The desktop app should use **mpv** for playback. The first playback milestone should use external `mpv` controlled through JSON IPC because it is faster to prove playback, subtitle handling, seeking, source loading, and watch progress without fighting native embedding immediately. The production target should move to **libmpv**, preferably through the render API where practical, because native window embedding can become platform-specific and especially annoying on macOS.

The app should avoid Electron and browser-runtime shells. Memory and performance are core product requirements, not late optimizations. The desktop UI should be lightweight, virtualized, cache-conscious, and able to handle large catalogs without loading everything into RAM.

---

## Product Goals

| Goal | Requirement |
|---|---|
| Desktop platform support | Windows, macOS, and Linux are first-class targets from phase 0 |
| Playback | mpv-backed playback with subtitles, seeking, progress, audio/subtitle track selection, and hardware decode where safe |
| Performance | No Electron; low idle memory; virtualized lists; disk-backed image cache; one active player instance |
| Reuse | Reuse or port NuvioMobile shared logic where realistic; avoid directly porting Android/iOS UI code |
| UX | Native-feeling desktop browsing with keyboard, mouse, trackpad, and optional TV-style focus mode |
| Packaging | Produce installable/testable artifacts for Windows, macOS, and Linux |
| Compliance | Preserve GPL obligations and include license/source distribution requirements in release flow |
| Safety | App remains a client for user-installed/user-provided sources; do not host, distribute, or bypass DRM/content protections |

---

## High-Level Fork Decision

| Source | Use For | Do Not Use For |
|---|---|---|
| `NuvioMobile` | Main fork base, app behavior, shared models, repositories, feature flows, addon/source handling references | Expecting Compose mobile UI to automatically become a finished desktop UI |
| `NuvioTV` | Reference for TV playback UX, big-screen browsing, focus model, player controls, and feature parity expectations | Direct Windows/macOS/Linux port base |
| `mpv` / `libmpv` | Playback backend dependency | Forking mpv unless a real player-core patch is needed |

**Decision:** Fork `NuvioMobile` first. Add a desktop project beside the existing mobile app or in a sibling repository under the fork. Keep `NuvioTV` checked out as a reference repo during parity work.

---

## Tech Stack

| Layer | Technology | Justification |
|---|---|---|
| Desktop UI | **Avalonia + C#/.NET** | Mature cross-platform desktop UI with Windows, macOS, and Linux support; lighter than Electron; good packaging story |
| App language | C# for desktop shell, Kotlin used as source/reference from NuvioMobile | Fast native desktop iteration while preserving mobile code as source of truth for behavior |
| Playback prototype | External `mpv` process + JSON IPC | Fastest way to prove stream loading, playback commands, subtitles, and watch progress on all three OSes |
| Playback production | `libmpv` via P/Invoke / generated bindings; render API preferred | Embedded playback, lower friction for packaged app, better UX than external window |
| Local database | SQLite | Watch history, settings, cached metadata, addon registry, and progress without cloud dependency |
| HTTP/networking | `HttpClient` + typed service layer | Lightweight, built-in, testable, no heavy runtime |
| JSON/schema validation | System.Text.Json + explicit DTO validators | Avoid runtime surprises when consuming addon manifests and metadata |
| Image cache | Disk cache + bounded memory cache | Prevent poster/backdrop grids from eating RAM |
| Logging | Serilog or Microsoft.Extensions.Logging | Structured logs for playback, addon resolution, and platform-specific failures |
| Testing | xUnit/NUnit + Playwright/Appium-style smoke where useful + platform CLI tests | Unit, integration, player contract, packaging, and cross-platform regression coverage |
| CI/CD | GitHub Actions matrix | Build/test/publish on Windows, macOS, and Ubuntu runners |
| Packaging | Windows: zip + MSIX/MSI later; macOS: `.app` + DMG; Linux: AppImage/tar.gz first, deb/rpm later | Start simple, then add native packaging as release stabilizes |

### Whole-Plan Stack Decision

**Decision: keep C#/.NET + Avalonia as the primary stack for the full implementation plan.**

Rust was evaluated for the whole desktop app and rejected for now. The plan is dominated by desktop UI, cross-platform packaging, headless UI tests, menus, dialogs, accessibility, SQLite-backed state, and mpv integration. C#/Avalonia is the lower-risk fit because it gives a mature Windows/macOS/Linux desktop path without adding Electron or a browser-shell runtime.

Rust remains allowed only behind a narrow boundary if a later phase proves it is needed:

- A small native helper process.
- A tightly scoped FFI/native interop module.
- A replacement for a measured C# interop or teardown bottleneck found in Phase 6 or Phase 9.

Rust must not replace the main app stack unless Avalonia fails a concrete gate such as cross-platform launch, rendering, packaging, video-surface integration, accessibility, or headless/component testing. Tauri is not an acceptable replacement under the current constraints because it depends on system WebViews, which conflicts with the no browser-shell runtime rule. Native Rust GUI stacks such as iced or Slint may be re-evaluated only if Avalonia fails one of those gates with evidence.

Sources considered for this decision: Avalonia platform support, Tauri WebView architecture, Slint desktop support/licensing, iced cross-platform GUI docs, and mpv/libmpv documentation.

---

## Platform Target Matrix

| Platform | Minimum MVP Target | Architecture Targets | Release Artifact |
|---|---|---|---|
| Windows | Windows 10 22H2+ / Windows 11 | `win-x64`, later `win-arm64` | portable `.zip` first, MSIX/MSI later |
| macOS | macOS 14+ preferred, macOS 13 best-effort | `osx-arm64`, `osx-x64` | `.app` bundle + unsigned DMG first, signed/notarized DMG later |
| Linux | Ubuntu/Debian/Fedora with X11 first; Wayland best-effort | `linux-x64`, later `linux-arm64` | tar.gz/AppImage first, deb/rpm later |

**Hard rule:** every milestone has a Windows, macOS, and Linux regression check. A feature is not considered done if it only works on one platform.

---

## Architecture Principles

1. **Do not directly port Android UI.** Treat mobile/TV UI as behavior reference, not as desktop layout.
2. **Keep player behind an interface.** The app should not care whether playback is external mpv IPC or embedded libmpv.
3. **Do not load full catalogs into memory.** Use paged repositories, virtualization, lazy images, and cancellation tokens.
4. **Keep platform-specific code isolated.** File paths, native libraries, packaging, window integration, notifications, and mpv loading belong in platform adapters.
5. **Make regression repeatable.** Every phase must include deterministic fixtures and OS-specific smoke tests.
6. **Prefer boring storage.** SQLite + disk cache beats clever in-memory state.
7. **Respect GPL and content boundaries.** Include licensing checks in release automation and avoid anything that enables unauthorized access or DRM bypass.
8. **Keep the primary app stack stable.** Do not restart the implementation around Rust, Tauri, iced, Slint, or another UI stack without a failed cross-platform gate and a written migration decision.
9. **Allow Rust only as a measured escape hatch.** Rust can be introduced only as an isolated helper or native interop module when Phase 6 or Phase 9 profiling proves C# interop, teardown, or performance is a real bottleneck.

---

## Project Structure

```text
nuvio-desktop/
  README.md
  LICENSE
  NOTICE
  docs/
    architecture.md
    platform-matrix.md
    packaging.md
    player-integration.md
    performance-budget.md
    legal-compliance.md
    regression-checklist.md

  upstream/
    NuvioMobile/                 # fork/submodule/reference checkout, depending on workflow
    NuvioTV/                     # reference only, not primary port base

  src/
    Nuvio.Desktop/
      Program.cs
      App.axaml
      App.axaml.cs
      appsettings.json
      Assets/
        Icons/
        Fonts/
      Views/
        ShellView.axaml
        HomeView.axaml
        SearchView.axaml
        CatalogView.axaml
        DetailsView.axaml
        PlayerView.axaml
        SettingsView.axaml
        AddonsView.axaml
      ViewModels/
        ShellViewModel.cs
        HomeViewModel.cs
        SearchViewModel.cs
        CatalogViewModel.cs
        DetailsViewModel.cs
        PlayerViewModel.cs
        SettingsViewModel.cs
        AddonsViewModel.cs
      Controls/
        PosterGrid.axaml
        MediaCard.axaml
        PlayerControls.axaml
        FocusRing.axaml
        LoadingState.axaml
        ErrorState.axaml

    Nuvio.Core/
      Models/
        AddonManifest.cs
        CatalogItem.cs
        MediaItem.cs
        StreamSource.cs
        WatchProgress.cs
        SubtitleTrack.cs
        PlaybackState.cs
      Services/
        AddonService.cs
        CatalogService.cs
        MetadataService.cs
        StreamResolver.cs
        WatchProgressService.cs
        SettingsService.cs
      Repositories/
        IAddonRepository.cs
        ICatalogRepository.cs
        IWatchProgressRepository.cs
      Caching/
        ImageCache.cs
        MetadataCache.cs
      Validation/
        AddonManifestValidator.cs
        StreamSourceValidator.cs

    Nuvio.Player/
      IPlayerEngine.cs
      PlayerCommand.cs
      PlayerEvent.cs
      PlayerOptions.cs
      ExternalMpv/
        ExternalMpvEngine.cs
        MpvIpcClient.cs
        MpvProcessLocator.cs
        MpvCommandSerializer.cs
      LibMpv/
        LibMpvEngine.cs
        LibMpvNative.cs
        LibMpvRenderHost.cs
        LibMpvLibraryLoader.cs
      Subtitles/
        SubtitleTrackMapper.cs
      Diagnostics/
        PlayerLogSink.cs

    Nuvio.Platform/
      IPlatformPaths.cs
      IPlatformPackagingInfo.cs
      Windows/
        WindowsPaths.cs
        WindowsMpvLocator.cs
      MacOS/
        MacOSPaths.cs
        MacOSMpvLocator.cs
      Linux/
        LinuxPaths.cs
        LinuxMpvLocator.cs

    Nuvio.Data/
      NuvioDbContext.cs
      Migrations/
      SQLite/
        WatchProgressStore.cs
        AddonStore.cs
        SettingsStore.cs

  tests/
    Nuvio.Core.Tests/
    Nuvio.Player.Tests/
    Nuvio.Data.Tests/
    Nuvio.Desktop.Tests/
    fixtures/
      addon-manifests/
      streams/
      metadata/
      images/

  packaging/
    windows/
      msix/
      wix/
    macos/
      Info.plist
      entitlements.plist
      make-dmg.sh
    linux/
      appimage/
      debian/
      rpm/

  scripts/
    bootstrap.ps1
    bootstrap.sh
    build-all.ps1
    build-all.sh
    verify-all.ps1
    verify-all.sh
    package-windows.ps1
    package-macos.sh
    package-linux.sh
    fetch-mpv-windows.ps1
    fetch-mpv-macos.sh
    fetch-mpv-linux.sh

  .github/
    workflows/
      ci.yml
      package.yml
      release.yml
```

---

## Core Interfaces

### Player Engine Contract

```csharp
public interface IPlayerEngine : IAsyncDisposable
{
    Task InitializeAsync(PlayerOptions options, CancellationToken ct);
    Task LoadAsync(StreamSource source, CancellationToken ct);
    Task PlayAsync(CancellationToken ct);
    Task PauseAsync(CancellationToken ct);
    Task SeekAsync(TimeSpan position, CancellationToken ct);
    Task SetVolumeAsync(int volume, CancellationToken ct);
    Task SelectAudioTrackAsync(string trackId, CancellationToken ct);
    Task SelectSubtitleTrackAsync(string? trackId, CancellationToken ct);
    IAsyncEnumerable<PlayerEvent> Events(CancellationToken ct);
}
```

### Stream Source Contract

```csharp
public sealed record StreamSource(
    string Id,
    Uri Url,
    string? Title,
    string? QualityLabel,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<SubtitleTrack> Subtitles,
    bool IsUserProvided
);
```

### Watch Progress Contract

```csharp
public sealed record WatchProgress(
    string MediaId,
    string? EpisodeId,
    TimeSpan Position,
    TimeSpan Duration,
    double Percent,
    DateTimeOffset UpdatedAt
);
```

---

## Player Strategy

### Milestone Player Path

| Stage | Implementation | Purpose |
|---|---|---|
| Stage 1 | External `mpv` process + JSON IPC | Prove playback quickly on Windows/macOS/Linux |
| Stage 2 | External `mpv` bundled or discovered per OS | Stabilize packaging and user setup |
| Stage 3 | Embedded `libmpv` with native bindings | Production UX with in-app video surface |
| Stage 4 | Render API path | Better long-term cross-platform rendering control |

### mpv Defaults

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

### Player Rules

- Only one active player instance at a time.
- Player events must be observed on a background thread and marshaled safely to the UI thread.
- Playback state must update watch progress no more than once every 5 seconds during playback and once on pause/stop/exit.
- Subtitles and audio tracks must be modeled in the app, not hardcoded into player UI.
- Player logs must be capturable for diagnostics without dumping sensitive URLs by default.
- External mpv mode must work even before embedded libmpv lands.

---

## Performance Budget

| Area | Target |
|---|---|
| Idle memory after cold start | Under 200 MB target, investigate above 250 MB |
| Catalog browsing memory | Stable after scrolling; no unbounded poster bitmap growth |
| Startup to interactive | Under 2.5 seconds on normal SSD desktop target |
| Poster grid scrolling | No visible UI thread stalls; virtualized items only |
| Playback CPU | mpv handles decode; UI overlays must not trigger heavy redraw loops |
| Disk cache | Configurable max size, default 500 MB or less |
| SQLite writes | Batched/debounced for progress and metadata cache |
| Network | Cancellation for stale search/catalog requests |

---

## Data and Cache Model

| Store | Purpose | Retention |
|---|---|---|
| SQLite `settings` | Theme, playback options, cache limits, language preferences | User controlled |
| SQLite `addons` | Installed addon manifests and enabled/disabled state | Until removed |
| SQLite `watch_progress` | Local progress by media/episode | Until cleared by user |
| SQLite `metadata_cache` | Title/details/cache metadata | TTL-based |
| Disk image cache | Posters, backdrops, thumbnails | LRU max-size eviction |
| Temp folder | Temporary stream/player files if needed | Cleared on startup/exit |

**No cloud sync in MVP.** Add cloud/account sync later only after the local desktop client is stable.

---

## Security and Legal Model

### Hard Invariants

- The app is a playback client for user-installed/user-provided sources.
- The app must not host, store, or distribute media content.
- The app must not bypass DRM, paywalls, authentication, or access controls.
- The app must preserve GPL license notices and source availability obligations when binaries are distributed.
- Release artifacts must include `LICENSE`, `NOTICE`, dependency license inventory, and source instructions.
- Logs must redact sensitive URLs, tokens, and headers by default.

### Threat Table

| Threat | Risk | Mitigation |
|---|---|---|
| Malicious addon manifest | Medium | Schema validation, URL validation, timeouts, no arbitrary code execution |
| Unbounded catalog response | Medium | Paging, max response size, cancellation, streaming parse where possible |
| Sensitive token leakage in logs | High | Header redaction and URL query redaction |
| Playback crash from bad stream | Medium | Player isolation, recovery UI, crash logs without sensitive data |
| Native library mismatch | Medium | Per-platform libmpv loader tests and startup diagnostics |
| GPL release mistake | High | Release checklist blocks packaging unless license/source bundle exists |
| Memory growth from posters | High | Disk cache + bounded memory cache + image disposal regression tests |
| macOS Gatekeeper friction | Medium | Signed/notarized DMG before public release |
| Linux dependency drift | Medium | Document packages; AppImage/tarball smoke tests on clean runners |

---

## Testing Strategy

### Test Matrix

| Layer | Test Type | Focus |
|---|---|---|
| Core models | Unit | Addon manifest parsing, stream source validation, metadata mapping |
| Repositories | Unit/integration | SQLite migrations, watch progress, settings persistence |
| Player IPC | Unit/integration | JSON command serialization, event parsing, process lifecycle |
| Player embedded | Integration | libmpv load, basic play/pause/seek, teardown without leaks |
| UI view models | Unit | Search flow, details flow, player state transitions |
| UI components | Component/snapshot where practical | Empty/loading/error states, virtualization behavior |
| Packaging | CI smoke | Artifact launches on Windows/macOS/Linux runners where possible |
| Performance | Manual + automated probes | Memory after scrolling, startup time, playback state stability |
| Compliance | Scripted checks | License files, dependency inventory, source archive availability |

### Cross-Platform Regression Policy

Every phase must include:

- Windows regression command.
- macOS regression command.
- Linux regression command.
- At least one player-related regression if the phase touches playback.
- At least one memory/performance regression if the phase touches UI lists, images, caching, or playback.
- At least one packaging or startup smoke test once native libraries are introduced.

### Standard Verification Commands

```bash
# All platforms
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release

# Runtime-specific publish examples
dotnet publish src/Nuvio.Desktop/Nuvio.Desktop.csproj -c Release -r win-x64 --self-contained true
dotnet publish src/Nuvio.Desktop/Nuvio.Desktop.csproj -c Release -r osx-arm64 --self-contained true
dotnet publish src/Nuvio.Desktop/Nuvio.Desktop.csproj -c Release -r linux-x64 --self-contained true

# Project-level wrappers
./scripts/verify-all.sh
./scripts/build-all.sh
./scripts/package-linux.sh

./scripts/verify-all.ps1
./scripts/build-all.ps1
./scripts/package-windows.ps1
```

---

## Planning Audit Additions

This plan was audited after Phase 0 repo scaffolding. The overall stack and phase order are sound, but future work should apply these corrections:

- Keep implementation guidance in this plan even if the file is local/ignored; keep public release files such as `README.md`, `LICENSE`, `NOTICE`, and compliance docs tracked.
- Phase 0 must include `Nuvio.Platform` in the solution, not only UI/core/player/data, because OS detection, native paths, and mpv discovery are cross-cutting from the start.
- Pin the SDK with `global.json`, then re-evaluate .NET SDK/Avalonia support before beta. Do not upgrade target frameworks during feature work without a build-matrix pass.
- Treat Avalonia GUI launch checks separately from headless/unit checks. CI may build and run Avalonia headless tests on every OS, while real window launch can start as a smoke/manual gate until stable automation exists.
- Add privacy and redaction gates before network/addon work, not after live source resolution exists.
- Use generated or clearly redistributable media fixtures only. Do not commit copyrighted sample videos or real private stream URLs.
- Introduce dependency license inventory automation before native packaging, because mpv/libmpv and bundled native assets can change release obligations.
- Add rollback/corruption handling for SQLite migrations before storing important user state.
- Keep external mpv as an operational fallback until embedded libmpv has repeated start/stop and packaging smoke coverage on all three platforms.
- Keep C#/.NET + Avalonia as the whole-plan primary stack. Rust was evaluated and should remain limited to a future helper or native interop module unless Avalonia fails a concrete cross-platform gate.
- Do not use Tauri while the no browser-shell runtime rule remains active, because WebView-based desktop shells are outside the accepted runtime model.

## Agent Skills by Phase

Use installed skills first. Install a new skill only when a phase has a clear gap that existing skills do not cover.

| Phase | Recommended skills | Notes |
|---|---|---|
| 0 | `find-skills`, `avalonia` | `find-skills` was used. `markpitt/claude-skills@avalonia` was installed because the plan uses Avalonia. |
| 1 | `security-best-practices`; optional `wshaddix/dotnet-skills@dotnet-testing-strategy` | Useful for DTO validation, redaction tests, and fixture strategy. |
| 2 | `avalonia`; optional C#/.NET skill | No strong mpv/libmpv-specific skill was found; use official mpv docs and repo tests. |
| 3 | `avalonia`, `ui-audit`, `adapt` | Use Avalonia guidance for MVVM, compiled bindings, navigation, and virtualized controls. |
| 4 | `security-best-practices` | Validation, URL safety, response limits, cancellation, and log redaction matter more than UI polish here. |
| 5 | SQLite/database design references; optional `.NET` testing skill | Keep SQLite patterns boring and migration behavior testable. |
| 6 | C# native interop references | No dedicated libmpv skill was found; keep this behind `IPlayerEngine`. |
| 7 | `avalonia`, `ui-audit`, `polish`, `adapt` | Use for responsive desktop layouts, focus, accessibility, and resize stability. |
| 8 | Optional `license-compliance-auditor`, optional GitHub Actions/release skill | Packaging needs compliance and release automation discipline. |
| 9 | `ui-audit`, optional performance-profiling skill | Use only after baseline probes exist so performance work is evidence-driven. |
| 10 | `security-best-practices`, optional `license-compliance-auditor`, optional release skill | Final beta gate should be compliance, privacy, packaging, and regression focused. |

---

## Phase 0: Fork, Repo Setup, and Desktop Architecture Skeleton

> Establish the fork, desktop repo layout, build system, CI matrix, and platform target policy before attempting real playback or UI parity.

**Suggested skills:** `find-skills`, `avalonia`; consider a GitHub Actions/release skill only if CI packaging becomes nontrivial.

**Avalonia skill usage:** Use the `avalonia` skill when creating the shell project, MVVM layout, compiled-binding defaults, headless smoke strategy, and first desktop window.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source and `upstream/NuvioTV` as the playback/TV UX reference. Fetch both into ignored `upstream/` checkouts and reuse layout, licenses, scripts, CI patterns, and documented app behavior as setup references.

- [x] 0.1 Fork `NuvioMobile` and create a long-lived `desktop-port` branch.
- [x] 0.2 Add `NuvioTV` as a reference checkout or document a local clone path for parity checks.
- [x] 0.3 Decide repo layout: desktop code inside the fork under `desktop/` or sibling repo `NuvioDesktop`. Prefer inside fork until shared behavior extraction is stable.
- [x] 0.4 Add `docs/architecture.md`, `docs/platform-matrix.md`, `docs/player-integration.md`, `docs/performance-budget.md`, and `docs/legal-compliance.md`.
- [x] 0.5 Add initial .NET solution with `Nuvio.Desktop`, `Nuvio.Core`, `Nuvio.Player`, `Nuvio.Platform`, `Nuvio.Data`, and test projects.
- [x] 0.6 Add Avalonia shell with one empty window and platform name display.
- [x] 0.7 Add GitHub Actions matrix for `windows-latest`, `macos-latest`, and `ubuntu-latest`.
- [x] 0.8 Add release target matrix for `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64`, and later `linux-arm64`.
- [x] 0.9 Add license inventory placeholder and release-blocking compliance checklist.
- [x] 0.10 Add baseline memory/startup measurement script.
- [x] 0.11 Add `global.json` and document supported SDK/runtime versions.
- [x] 0.12 Add an Avalonia headless-first UI smoke strategy so CI does not depend on fragile real-window automation too early.

**Phase 0 completion note:** completed locally on the `desktop-port` branch using the sibling `NuvioDesktop` repo layout. `upstream/NuvioMobile` and `upstream/NuvioTV` are ignored local reference checkouts. Windows verification passes with an Avalonia headless shell smoke test, and local publish checks pass for `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, and `linux-x64`. `linux-arm64` remains a later target as planned.

- **Verify**: the empty Avalonia desktop shell builds and launches on Windows, macOS, and Linux; CI runs the same build/test command on all three OSes.
- **Regression**:
  - CI: Windows build succeeds.
  - CI: macOS build succeeds.
  - CI: Linux build succeeds.
  - Unit: platform detection returns expected platform family.
  - Build: Release publish succeeds for `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, and `linux-x64`.
  - Compliance: release checklist fails if `LICENSE`/`NOTICE` are missing.

### Phase 0 Regression Gate

- GitHub Actions matrix is green on Windows, macOS, and Linux.
- Empty Avalonia window launches locally on at least one machine and in smoke mode on CI where possible.
- Platform target matrix is documented.
- Release/compliance checklist exists.
- No playback code is started until this foundation is stable.

---

## Phase 1: Shared Domain Model and NuvioMobile Behavior Mapping

> Identify what can be reused or ported from NuvioMobile before building desktop features. This prevents rewriting behavior blindly.

**Suggested skills:** `security-best-practices` for redaction and validation review; optional `.NET testing` skill if fixtures grow complex.

**Avalonia skill usage:** Do not use Avalonia for core domain implementation except to confirm DTO/view-model boundaries stay UI-independent.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source for domain models, repositories, addon/source contracts, validation rules, metadata mapping, watch-progress semantics, settings concepts, and fixture structures. Use `upstream/NuvioTV` as the playback/TV UX reference for player-control parity notes.

- [x] 1.1 Inventory NuvioMobile `commonMain` models, repositories, feature flows, addon handling, source resolution, metadata handling, and watch progress flow.
- [x] 1.2 Create a mapping document: `NuvioMobile source -> Nuvio.Desktop equivalent`.
- [x] 1.3 Define C# DTOs for addon manifests, catalogs, media items, stream sources, subtitles, and playback state.
- [x] 1.4 Add fixture addon manifests from safe test data.
- [x] 1.5 Implement manifest parsing and validation in `Nuvio.Core`.
- [x] 1.6 Implement basic metadata and catalog models without UI.
- [x] 1.7 Implement stream source model with headers, subtitles, quality labels, and user-provided flag.
- [x] 1.8 Add `NuvioTV` parity notes for player controls and TV-specific UX that may be useful later.
- [x] 1.9 Add architecture decision record explaining why desktop UI is rebuilt in Avalonia instead of directly porting Android TV UI.
- [x] 1.10 Add redaction helpers for logs that may contain stream URLs or headers.
- [x] 1.11 Define response-size, timeout, URL-scheme, and header allowlist rules before implementing live addon/network calls.
- [x] 1.12 Record which NuvioMobile behavior is copied, adapted, or intentionally dropped so parity decisions are auditable.

**Phase 1 implementation note:** Core-only DTOs, parsers, deterministic fixtures, validation defaults, redaction helpers, behavior mapping docs, player parity notes, and the Avalonia rebuild ADR are implemented. Local Windows verification is required before handoff; macOS/Linux remain covered by the CI matrix or Unix wrapper runs.

**Phase 1 audit note:** rechecked against `upstream/NuvioMobile` before handoff. Addon manifest parsing, resource/catalog fallback behavior, stream filtering, proxy header mapping, metadata DTO shape, player snapshot state, and watch-progress thresholds are ported as directly as practical into C#. Extra logic is limited to Phase 1 safety gates: typed `Uri` boundaries, explicit validation errors, manifest URL policy, fetch-limit constants for later live network work, and log redaction. Playback stream URLs intentionally remain permissive like upstream and are not forced to HTTPS.

- **Verify**: core models parse deterministic fixtures and produce stable validated source objects without launching UI or player.
- **Regression**:
  - Unit: valid addon manifest fixture parses.
  - Unit: invalid manifest fixture fails with clear error.
  - Unit: stream headers are preserved for playback but redacted in logs.
  - Unit: subtitle list maps into internal `SubtitleTrack` objects.
  - Cross-platform: unit suite passes on Windows, macOS, and Linux.

### Phase 1 Regression Gate

- Behavior mapping document is complete enough to start implementation.
- Core DTOs and validators exist.
- Test fixtures exist.
- All core tests pass on Windows, macOS, and Linux.
- Logs redact sensitive playback values by default.

---

## Phase 2: External mpv Prototype with JSON IPC

> Prove end-to-end playback on all three desktop OSes before embedding libmpv.

**Suggested skills:** `avalonia` for diagnostics UI; no strong mpv-specific skill was found, so prefer official mpv JSON IPC docs and local integration tests.

**Avalonia skill usage:** Use the `avalonia` skill for the player diagnostics panel and any playback-state UI binding, but keep IPC/player logic outside Avalonia.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source for stream loading, subtitles/audio tracks, progress reporting, player errors, and autoplay expectations. Use `upstream/NuvioTV` as the playback/TV UX reference for controls, focus, and player diagnostics.

- [x] 2.1 Add `IPlayerEngine` interface.
- [x] 2.2 Implement `ExternalMpvEngine` that starts `mpv` as a child process.
- [x] 2.3 Implement `MpvProcessLocator` for Windows, macOS, and Linux.
- [x] 2.4 Implement JSON IPC client with command send, event receive, timeout, reconnect/error handling.
- [x] 2.5 Support commands: load, play, pause, seek, stop, set volume, set fullscreen, select audio track, select subtitle track.
- [x] 2.6 Support observed properties: pause state, time position, duration, file loaded, end file, buffering/cache state, selected tracks.
- [x] 2.7 Add basic Avalonia player diagnostics panel showing player availability and version.
- [x] 2.8 Add fixture playback test using a tiny local sample file or generated test media.
- [x] 2.9 Add safe process teardown and orphan-process cleanup.
- [x] 2.10 Document platform setup for installing mpv during development.
- [x] 2.11 Add a redistributable/generated media fixture policy and never commit real private stream URLs.
- [x] 2.12 Add player log redaction tests before logging IPC commands or stream load failures.

- **Verify**: each OS can launch external mpv, load a test file/stream, play, pause, seek, and report progress back to the app.
- **Regression**:
  - Unit: JSON commands serialize exactly as expected.
  - Unit: IPC events parse into `PlayerEvent` objects.
  - Integration: external mpv starts and exits cleanly on Windows.
  - Integration: external mpv starts and exits cleanly on macOS.
  - Integration: external mpv starts and exits cleanly on Linux.
  - Integration: `SeekAsync` updates observed position.
  - Process: no orphan mpv process remains after app exit.

### Phase 2 Regression Gate

- External mpv playback works on Windows, macOS, and Linux.
- Player commands and events are covered by tests.
- Process teardown is reliable.
- The Avalonia UI can display playback state from mpv.
- No embedded libmpv work starts until external playback is stable.

**Phase 2 completion note:** external mpv setup, JSON IPC, commands, observed event mapping, teardown, generated-fixture integration coverage, redaction tests, MPV Manager setup-only policy, Avalonia diagnostics, and docs are implemented. The CI matrix installs mpv on Windows, macOS, and Linux and runs the opt-in `NUVIO_RUN_MPV_INTEGRATION=1` IPC/playback checks. Local Windows verification passes without requiring mpv on the developer machine; run the integration flag locally when mpv is installed.

---

## Phase 3: Desktop Shell, Navigation, and First Usable UI

> Build a lightweight desktop UI that can browse test catalogs, open details, and play using the external mpv engine.

**Suggested skills:** `avalonia`, `ui-audit`, `adapt`; use these for MVVM, virtualization, focus, and resize checks.

**Avalonia skill usage:** Use the `avalonia` skill for navigation, MVVM, compiled bindings, virtualization, responsive desktop layout, commands, focus, and headless/component tests.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source for Home, Search, Catalog, Details, Addons, Settings, and Player flows. Use `upstream/NuvioTV` as the playback/TV UX reference for player route behavior and optional focus-mode expectations.

- [x] 3.1 Build Avalonia shell layout: sidebar/topbar, content area, status bar, and player route.
- [x] 3.2 Add Avalonia navigation routes: Home, Search, Catalog, Details, Addons, Settings, Player.
- [x] 3.3 Add Avalonia MVVM structure with compiled bindings and cancellation-aware async commands.
- [x] 3.4 Add Avalonia global loading, empty, and error states.
- [x] 3.5 Implement Avalonia poster grid with virtualization enabled.
- [x] 3.6 Implement Avalonia details page with metadata, poster/backdrop, stream list, and play action.
- [x] 3.7 Implement basic Avalonia player view with external mpv state and controls.
- [x] 3.8 Add Avalonia keyboard shortcuts: Space play/pause, arrows seek, Esc back/exit fullscreen, Ctrl/Cmd+F search.
- [x] 3.9 Add Avalonia/platform-aware menu conventions: Windows/Linux menu shortcuts and macOS app menu behavior.
- [x] 3.10 Add Avalonia UI smoke tests for navigation without real network.
- [x] 3.11 Add Avalonia headless/component tests before requiring OS GUI automation in CI.
- [x] 3.12 Add explicit image sizing rules for posters/backdrops to prevent layout churn and bitmap over-allocation.

- **Verify**: a user can open the app, browse fixture catalog items, open details, start playback through mpv, pause/seek, and return to browsing.
- **Regression**:
  - Component: Avalonia poster grid renders 1,000 fixture items without creating 1,000 realized controls.
  - Unit: Avalonia navigation state transitions are deterministic.
  - Unit: cancellation cancels stale search requests.
  - Integration: details -> player route passes selected stream to `IPlayerEngine`.
  - UI smoke: Home/Search/Details/Player render on Windows.
  - UI smoke: Home/Search/Details/Player render on macOS.
  - UI smoke: Home/Search/Details/Player render on Linux.

### Phase 3 Regression Gate

- First usable desktop shell works with fixture data.
- Avalonia UI virtualization is verified.
- Playback route uses `IPlayerEngine`, not direct mpv calls.
- Keyboard shortcuts work.
- Windows, macOS, and Linux smoke checks pass.

**Phase 3 completion note:** the fixture desktop shell now has the sidebar/topbar/content/status layout, Home/Search/Catalog/Details/Addons/Settings/Player routes, compiled-binding Avalonia views, debounced/cancellation-aware search, global loading/empty/error states, fixed-size poster/backdrop surfaces, a virtualized poster-row grid, details stream play actions, and a player route backed by `IPlayerEngine`. Headless Avalonia regression tests cover deterministic navigation, stale search cancellation, details-to-player handoff, keyboard shortcuts, platform-aware Ctrl/Cmd search gestures, Home/Search/Details/Player rendering, and a 1,000-item catalog virtualization smoke. Cross-platform coverage is provided by the existing Windows/macOS/Linux CI matrix running the same headless/component tests.

---

## Phase 4: Addon, Catalog, Metadata, and Source Resolution

> Replace fixture data with real app logic adapted from NuvioMobile behavior, while keeping strict validation and cancellation.

**Suggested skills:** `security-best-practices`; use validation/schema guidance as a review lens, but keep implementation in C# DTO validators.

**Avalonia skill usage:** Use the `avalonia` skill only for source-list UI, loading/empty/error presentation, and cancellation-safe view-model binding; keep addon/network logic in core services.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source for addon installation, manifest parsing, catalog/search/details/source-resolution behavior, edge cases, and user-visible errors. Use `upstream/NuvioTV` as the playback/TV UX reference for source-selection and playback-entry expectations.

- [x] 4.1 Implement addon install/remove/enable/disable local store.
- [x] 4.2 Implement addon manifest fetch with timeout, size limit, and schema validation.
- [x] 4.3 Implement catalog fetch and pagination.
- [x] 4.4 Implement search across enabled addons.
- [x] 4.5 Implement metadata/details retrieval.
- [x] 4.6 Implement source resolution flow returning validated `StreamSource` objects.
- [x] 4.7 Add request cancellation when user changes query or navigates away.
- [x] 4.8 Add Avalonia source list UI with quality labels, provider labels, and error handling.
- [x] 4.9 Add network diagnostics for failed addon/source calls.
- [x] 4.10 Add cache policy for metadata and image URLs.
- [x] 4.11 Add per-host request limits, max response bytes, redirect policy, and retry/backoff rules.
- [x] 4.12 Add privacy-safe diagnostics that do not persist sensitive URLs, tokens, cookies, or headers.

- **Verify**: enabled addon fixtures can populate catalog/search/details/source flows and feed playable stream sources to the player engine.
- **Regression**:
  - Unit: malformed addon URL rejected.
  - Unit: oversized manifest rejected.
  - Unit: source resolution maps headers/subtitles correctly.
  - Integration: disabled addon is not queried.
  - Integration: search cancellation prevents stale results from overwriting newer results.
  - Integration: catalog pagination does not load all pages into memory.
  - Cross-platform: network tests pass on Windows, macOS, and Linux.

### Phase 4 Regression Gate

- Addon registry works locally.
- Catalog/search/details/source resolution work with fixtures and safe live test endpoints where available.
- All network flows have timeouts and cancellation.
- No source logic bypasses validation.
- Cross-platform regression suite passes.

---

## Phase 5: SQLite Storage, Watch Progress, and Cache System

> Add durable local state without cloud sync: settings, addons, progress, metadata cache, and image cache.

**Suggested skills:** SQLite/database design references; optional `.NET testing` skill for migration and integration-test structure.

**Avalonia skill usage:** Use the `avalonia` skill for cache/settings UI and clear-cache interactions; keep persistence and migrations independent of Avalonia.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source for settings, progress, library, addon, metadata, and cache semantics. Use `upstream/NuvioTV` as the playback/TV UX reference for resume/progress expectations and settings that affect playback.

- [x] 5.1 Add SQLite database project and migrations.
- [x] 5.2 Add settings store for theme, playback preferences, cache size, and player mode.
- [x] 5.3 Add addon store.
- [x] 5.4 Add watch progress store keyed by media/episode.
- [x] 5.5 Add metadata cache with TTL.
- [x] 5.6 Add disk-backed image cache with LRU eviction.
- [x] 5.7 Add bounded memory cache for decoded images.
- [x] 5.8 Add Avalonia cache settings UI and clear-cache action.
- [x] 5.9 Add progress updates from player events, debounced during playback and flushed on pause/stop/exit.
- [x] 5.10 Add startup migration and corruption recovery strategy.
- [x] 5.11 Add migration rollback/backup behavior for failed startup migrations.
- [x] 5.12 Add a data retention policy for progress, metadata cache, image cache, and diagnostic logs.

- **Verify**: settings, addons, metadata, images, and progress persist across app restarts without unbounded memory growth.
- **Regression**:
  - Unit: migrations create expected schema.
  - Unit: progress save/load round trips.
  - Unit: image cache evicts oldest entries after max size.
  - Unit: decoded image memory cache respects item limit.
  - Integration: watch progress updates at most once per debounce interval during playback.
  - Integration: clear cache removes disk entries but not user settings.
  - Cross-platform: database path is correct on Windows, macOS, and Linux.

### Phase 5 Regression Gate

- SQLite migrations work on all platforms.
- Watch progress survives restart.
- Image cache does not grow without limit.
- Disk paths follow platform conventions.
- Memory after scrolling returns near baseline after GC/cache trim.

**Phase 5 completion note:** live mode now opens SQLite under `Nuvio.Platform` paths, applies hand-written migrations, backs up before migration, restores after failed migration, and moves corrupt databases aside. SQLite-backed stores cover settings, addons, watch progress, and metadata TTL cache; image caching uses hashed URL keys, disk LRU eviction, and a bounded Avalonia decoded-image cache. The Settings route now exposes theme/player/cache controls and a clear-cache command that preserves settings, addons, and watch progress. Player progress is recorded through a dedicated recorder with 5-second write debouncing and flushes on pause, stop, errors, end, return, dispose, and replacement. Docs updated: `docs/architecture.md`, `docs/performance-budget.md`, and `docs/regression-checklist.md`. Verified on Windows with `./scripts/verify-all.ps1` after the Phase 5 changes.

---

## Phase 6: Embedded libmpv Spike and Render Integration

> Move from external mpv to in-app playback, but keep external mpv as fallback until embedded playback is stable on all three OSes.

**Suggested skills:** no dedicated libmpv skill found; use C# native interop references and keep the implementation behind `IPlayerEngine`.

**Avalonia skill usage:** Use the `avalonia` skill for render-host integration, player surface lifecycle, UI-thread marshaling, and player-control bindings; keep libmpv interop behind `IPlayerEngine`.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source for player parity expectations, track/subtitle handling, resume/progress rules, and playback errors. Use `upstream/NuvioTV` as the playback/TV UX reference for controls, focus, fullscreen, and big-screen playback behavior.

- [x] 6.1 Add `LibMpvNative` P/Invoke bindings for core client API.
- [x] 6.2 Add per-platform native library loader for `mpv-1.dll`, `libmpv.dylib`, and `libmpv.so`.
- [x] 6.3 Add startup diagnostics that detect missing/incompatible libmpv.
- [x] 6.4 Implement `LibMpvEngine` behind `IPlayerEngine`.
- [x] 6.5 Implement basic libmpv playback without custom rendering first where practical.
- [x] 6.6 Spike render API integration with Avalonia `OpenGlControlBase` or an equivalent render surface.
- [x] 6.7 Add fallback switch: `ExternalMpvEngine` if embedded engine fails.
- [x] 6.8 Add track selection, subtitle selection, seek, pause, volume, and progress parity with external mpv.
- [x] 6.9 Add teardown/leak test for repeated player create/destroy cycles.
- [x] 6.10 Document known limitations per OS.
- [x] 6.11 Define the supported libmpv version/ABI matrix before bundling native libraries.
- [x] 6.12 Add fallback acceptance criteria so embedded playback failures never strand the user without external mpv.

**Phase 6 completion note:** implemented as a spike with fallback on the `desktop-port` branch. `LibMpvLibraryLocator`/`LibMpvDiscoveryResult` (`Nuvio.Platform`) discover the shared library (env override → app-managed → system → loader-resolved) without bundling binaries; `LibMpvNativeLibraryLoader` (`Nuvio.Platform`) performs the real native handle load, and `LibMpvLibraryLoader` (`Nuvio.Player`) registers the `DllImportResolver`. `LibMpvNative` provides client + OpenGL render-API P/Invoke. `LibMpvEngine` implements `IPlayerEngine` and `IPlayerRenderSource`, runs an `mpv_wait_event` pump, and emits the same `PlayerEvent` records as `ExternalMpvEngine` (audio-first; render layered separately). The render spike is `LibMpvVideoView : OpenGlControlBase` + `LibMpvRenderSession`, with the engine freeing the render context before `mpv_terminate_destroy` and degrading gracefully on failure. `SelectingPlayerEngineFactory` honors the `PlayerMode` setting and falls back to external mpv when libmpv is unavailable; the Settings page now exposes "Embedded libmpv (experimental)" and restores the saved mode. External mpv remains the default and guaranteed fallback. Locator filename/discovery, render-source host, and factory-fallback unit tests run on any host; opt-in `NUVIO_RUN_LIBMPV_INTEGRATION=1` smokes cover init/load/play/seek/teardown and repeated create/dispose. Docs updated: `docs/player-integration.md`, `docs/regression-checklist.md`. Verified on Windows with `./scripts/verify-all.ps1`.

- **Verify**: embedded libmpv can load, play, pause, seek, and shut down cleanly on Windows, macOS, and Linux, or automatically fall back to external mpv with a clear diagnostic.
- **Regression**:
  - Unit: native library loader selects correct filename per OS.
  - Integration: libmpv initializes on Windows.
  - Integration: libmpv initializes on macOS.
  - Integration: libmpv initializes on Linux.
  - Integration: repeated initialize/play/stop/dispose cycles do not leak obvious process memory.
  - Integration: embedded and external player engines emit equivalent high-level events for the same test file.
  - UI: player controls work with both engines.

### Phase 6 Regression Gate

- Embedded libmpv works on all three OSes or has documented fallback gaps.
- External mpv fallback remains working.
- Player engine abstraction prevents UI duplication.
- Repeated teardown does not show obvious leaks.
- Render integration path is documented before replacing prototype playback.

---

## Phase 7: Desktop UX Hardening and Platform Polish

> Make the app feel like a real desktop client instead of a mobile app inside a desktop window.

**Suggested skills:** `avalonia`, `ui-audit`, `adapt`, `polish`; use them for accessibility, focus, responsive layout, and UI stability.

**Avalonia skill usage:** Use the `avalonia` skill for responsive layouts, focus states, menus, dialogs, theme handling, fullscreen, accessibility labels, high-DPI behavior, and resize stress checks.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source for terminology, empty/error states, localization strings, settings, and user flows. Use `upstream/NuvioTV` as the playback/TV UX reference for focus mode, playback ergonomics, and big-screen behavior.

- [x] 7.1 Add responsive Avalonia desktop layouts for small laptop, normal desktop, and TV/large-screen window sizes.
- [x] 7.2 Add Avalonia keyboard-first navigation and focus states.
- [x] 7.3 Add optional Avalonia TV-style focus mode inspired by NuvioTV.
- [x] 7.4 Add Avalonia/native file dialogs where needed.
- [x] 7.5 Add Avalonia system theme handling while preserving explicit app theme setting.
- [x] 7.6 Add Avalonia platform menus: macOS app menu, Windows/Linux menu accelerators.
- [x] 7.7 Add Avalonia fullscreen behavior per platform.
- [x] 7.8 Add Avalonia mini-player or compact player layout only if it does not increase complexity too much.
- [x] 7.9 Add Avalonia accessibility labels for major controls.
- [x] 7.10 Add Avalonia high-DPI testing for posters and player controls.
- [x] 7.11 Add Avalonia empty/error copy that helps users fix missing mpv/libmpv/addon/network issues.
- [x] 7.12 Add Avalonia accessibility name/role checks for navigation, player controls, dialogs, and settings.
- [x] 7.13 Add Avalonia window-resize stress checks for catalog grids, details pages, and player overlays.

**Phase 7 completion note:** implemented on the `desktop-port` branch. Theme (7.5) is a full token refactor: `Styles/Theme.axaml` carries Light/Dark `ThemeDictionaries`, all 9 views + `MainWindow` consume tokens via `DynamicResource`, and `IThemeController`/`ThemeController` apply the persisted `ThemeMode` to `Application.RequestedThemeVariant` (System → `Default`/OS-following) at startup and live from the Settings combo. Shared controls (`Controls/StateOverlay`, `PosterCard`, `PlayerControls` with templates in `Styles/Controls.axaml`) replace inline copy-paste; `StateOverlay` carries the actionable mpv/addon/network recovery copy (7.11). Responsive `DesktopLayoutMode` (Compact/Normal/Wide/Tv) is recomputed on `SizeChanged` and collapses the nav rail + sizes poster cards (7.1/7.13). Keyboard-first nav + a shared `FocusAdorner` focus ring cover the full search→play→exit workflow incl. F11 (7.2). Fullscreen is promoted to `WindowState.FullScreen` with chrome hidden and prior state restored (7.7). macOS `NativeMenu` (built in `MainWindow.BuildNativeMenu`) + Windows/Linux in-window accelerators (7.6); "Open media file…" via `StorageProvider` → `MainWindowViewModel.PlayLocalFileAsync` (7.4). TV focus mode adds `DesktopSettings.TvFocusMode` (JSON round-trips through `SqliteSettingsStore`) forcing the Tv layout (7.3); mini-player reuses `PlayerControls` via `PlayerViewModel.IsMiniMode`, one engine instance (7.8). `AutomationProperties.Name` on nav/back/search/player/settings controls (7.9/7.12). High-DPI `ImageDecodeSizing` bounds poster/backdrop decode width by layout × scaling, with decode width in the decoded-cache key (7.10). New headless regressions live in `tests/Nuvio.Desktop.Tests/ViewModels/Phase7UxHardeningTests.cs`; docs updated in `docs/architecture.md`, `docs/player-integration.md`, `docs/regression-checklist.md`. Verified on Windows with `./scripts/verify-all.ps1`; macOS/Linux covered by the CI matrix running the same headless tests.

- **Verify**: navigation, focus, fullscreen, and settings feel correct on Windows, macOS, and Linux with mouse and keyboard.
- **Regression**:
  - Avalonia UI: keyboard route can search, select media, start playback, pause, seek, and exit playback.
  - Avalonia UI: focus ring is visible on all interactive controls.
  - Avalonia UI: fullscreen enter/exit works on Windows.
  - Avalonia UI: fullscreen enter/exit works on macOS.
  - Avalonia UI: fullscreen enter/exit works on Linux.
  - Avalonia UI: high-DPI poster grid remains crisp and does not over-allocate bitmaps.
  - Accessibility: major controls expose labels/names.

### Phase 7 Regression Gate

- Desktop UX no longer feels like a direct mobile port.
- Keyboard and mouse workflows are both complete.
- Fullscreen behavior is stable per OS.
- Avalonia focus/accessibility smoke checks pass.
- Large poster grids remain performant.

---

## Phase 8: Packaging, Native Dependencies, and Installers

> Produce repeatable installable artifacts for every desktop OS.

**Suggested skills:** optional `license-compliance-auditor` and GitHub Actions/release skill; only install if release automation becomes hard to maintain manually.

**Avalonia skill usage:** Use the `avalonia` skill for desktop publish/launch smoke expectations and Avalonia app packaging behavior; keep installer tooling OS-native.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source for release notes structure, GPL/source-distribution patterns, versioning conventions, and dependency-notice practices. Use `upstream/NuvioTV` as the playback/TV UX reference for packaged playback expectations and known installer/support patterns.

- [x] 8.1 Add `dotnet publish` scripts for each runtime identifier. (RID-parameterized `scripts/package-*`; defaults win-x64/osx-arm64/linux-x64.)
- [x] 8.2 Decide whether to bundle mpv/libmpv or require user-installed mpv for each release channel. (Decision: **do not bundle**; rely on the `Nuvio.Platform` discovery chain — `docs/native-dependency-provenance.md`.)
- [x] 8.3 Windows: create portable zip first. (`scripts/package-windows.ps1` → `nuvio-desktop-<version>-win-x64.zip`, unsigned.)
- [ ] 8.4 Windows: add MSIX or MSI packaging after zip is stable. **Deferred to post-MVP backlog** (with Authenticode signing).
- [x] 8.5 macOS: create `.app` bundle with `Info.plist`, icon, executable permissions, and embedded native dependencies. (Bundle assembled by `scripts/package-macos.sh`; mpv/libmpv intentionally not embedded — discovered at runtime.)
- [x] 8.6 macOS: add signing and notarization for public distribution. (Scripted local `codesign`/`notarytool`/`stapler` gated on `NUVIO_MACOS_*` env vars; CI stays unsigned.)
- [x] 8.7 Linux: create tar.gz portable package first. (`scripts/package-linux.sh` → `nuvio-desktop-<version>-linux-x64.tar.gz`.)
- [ ] 8.8 Linux: add AppImage after tar.gz is stable. **Deferred to post-MVP backlog.**
- [ ] 8.9 Linux: add deb/rpm packaging if there is demand. **Deferred to post-MVP backlog.**
- [x] 8.10 Add Avalonia artifact smoke tests: launch, open settings, detect player, exit. (Headless `--self-check` startup path in `Program.cs`; packagers + `package.yml` run the published binary with `--self-check`.)
- [x] 8.11 Add dependency license inventory generation. (`scripts/generate-license-inventory.*` regenerates `docs/dependency-licenses.md`; no `TBD`; curated map with graph-drift guard.)
- [x] 8.12 Add source bundle generation for GPL release compliance. (`scripts/make-source-bundle.*` via `git archive`, excludes `upstream/`; wired into `release.yml`.)
- [x] 8.13 Add artifact contents tests that assert `LICENSE`, `NOTICE`, dependency inventory, and source instructions are included. (`Phase8PackagingTests` + extended `ComplianceFilesTests`; artifact-contents gated by `NUVIO_ARTIFACT_DIR`.)
- [x] 8.14 Add native dependency provenance notes for every bundled mpv/libmpv binary. (`docs/native-dependency-provenance.md`; none bundled today, with a per-binary table to fill if one is ever dropped in.)

> **Phase 8 completion note.** Packaging produces labelled, compliance-complete,
> launch-smoked artifacts per OS (`scripts/package-{windows.ps1,macos.sh,linux.sh}`):
> Windows portable `.zip` (unsigned), Linux `.tar.gz`, macOS `.app` + DMG (signed +
> notarized locally when `NUVIO_MACOS_*` env vars are present, otherwise unsigned in CI).
> Versioning is centralized in `Directory.Build.props` (default `0.8.0`, `-p:Version=`
> overridable). mpv/libmpv is **not** bundled — the app uses the existing
> `MpvProcessLocator`/`LibMpvLibraryLocator` discovery chain, documented in
> `docs/native-dependency-provenance.md`. GPL source availability is a written offer plus
> a reproducible `git archive` tarball (`docs/source-availability.md`,
> `scripts/make-source-bundle.*`). The license inventory is generated with no `TBD`
> (`scripts/generate-license-inventory.*`). A headless `--self-check` path is the
> launch/exit smoke. Strategy and gates: `docs/packaging.md` and the Phase 8 Gate in
> `docs/regression-checklist.md`.
>
> **Deferred to post-MVP backlog (not in Phase 8):** Windows MSIX/MSI (8.4) and
> Authenticode signing; Linux AppImage (8.8) and deb/rpm (8.9); assembly
> trimming/single-file/size optimization (revisited as a Phase 9 size investigation).
>
> **Verified on Linux:** `dotnet build`/`dotnet test` (Release), `verify-all.sh`,
> `generate-license-inventory.sh` (no `TBD`), `package-linux.sh` (artifact + `--self-check`
> exit 0), `make-source-bundle.sh` (excludes `upstream/`), and the `NUVIO_ARTIFACT_DIR`
> artifact-contents gate. **Verified by inspection only (not runnable here):** the Windows
> `.ps1` packager, the macOS `.app`/DMG assembly, and macOS signing/notarization.

- **Verify**: downloadable artifacts exist for Windows, macOS, and Linux and can launch on clean-ish machines/runners with clear player dependency behavior.
- **Regression**:
  - Packaging: `win-x64` artifact contains required DLLs and license files.
  - Packaging: `osx-arm64` artifact has `.app` structure and executable permissions.
  - Packaging: `linux-x64` artifact contains required native libraries or documents system packages.
  - Smoke: packaged app launches and exits on Windows.
  - Smoke: packaged app launches and exits on macOS.
  - Smoke: packaged app launches and exits on Linux.
  - Compliance: release job fails if source bundle or license inventory is missing.

### Phase 8 Regression Gate

- Artifacts build for all three platforms.
- License/source bundle is generated.
- Packaged smoke tests pass where CI allows.
- Player dependency story is clear to users.
- macOS signing/notarization is planned before public release.

---

## Phase 9: Performance, Memory, and Stability Hardening

> Optimize what actually matters: startup, browsing, poster grids, player transitions, and memory stability.

**Suggested skills:** `ui-audit`; optional performance-profiling skill after baseline probes exist.

**Avalonia skill usage:** Use the `avalonia` skill when profiling UI-thread stalls, virtualization, image sizing, bindings, render invalidation, and resize behavior.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source for scrolling, image loading, cache behavior, playback recovery, and search/source-resolution stability. Use `upstream/NuvioTV` as the playback/TV UX reference for player transition and big-screen stress flows.

- [x] 9.1 Add startup timing probe. (`--perf-probe` cold-start scenario in `PerfProbe.cs`; `scripts/measure-perf.*`.)
- [x] 9.2 Add memory probe after cold start, after catalog scroll, after details open/close loop, and after playback stop. (Four `--perf-probe` scenarios; managed-heap deltas as JSON.)
- [x] 9.3 Add poster grid stress test with thousands of fixture items. (`Phase9PerformanceTests`: 5,000 items, virtualization cap held during scroll.)
- [x] 9.4 Add image cache pressure test. (`Phase9PerformanceTests`: decoded LRU item cap + disk LRU byte cap.)
- [x] 9.5 Add playback start/stop loop test. (Fake-engine no-leak loop in `Phase9PerformanceTests`; gated real-mpv orphan-process loop + libmpv create/dispose loop.)
- [x] 9.6 Add network cancellation stress test. (`Phase9PerformanceTests`: 200-search cancellation storm, newest-wins.)
- [ ] 9.7 Profile Avalonia UI thread stalls during scrolling. (Open: virtualization bounds realized containers ≤ 250, but a real-window UI-thread/frame-time profiling note is still required.)
- [x] 9.8 Tune decoded image sizes and cache limits. (Evidence-driven: within budget, no change; byte-aware decoded cap logged as follow-up.)
- [x] 9.9 Tune mpv cache settings based on real playback behavior. (Measurement path added via gated playback-stop + `NUVIO_PERF_MEDIA_URL`; researched defaults retained pending real-stream data.)
- [x] 9.10 Add performance budget documentation and release gate. (`docs/performance-budget.md` measured budgets + release gate; report-only CI `perf` job.)
- [x] 9.11 Add before/after performance report templates so optimizations are tied to measured regressions. (`docs/perf/perf-report-template.md` + `docs/perf/README.md` + baseline.)
- [x] 9.12 Add stress tests for cancellation storms, image-cache pressure, and repeated playback teardown. (Covered across `Phase9PerformanceTests` + gated player loops.)

**Phase 9 implementation note:** implemented on the `desktop-port` branch as a measure-first harness with
report-only absolute budgets and hard-failing invariants. `PerfProbe` (`src/Nuvio.Desktop/PerfProbe.cs`,
wired into `Program.cs` next to `--self-check`) is a headless `--perf-probe` mode that boots the live service
graph and emits JSON for cold-start, catalog-scroll, details-loop, and a gated playback-stop scenario
(`elapsedMs` + `GC.GetTotalMemory` deltas); `scripts/measure-perf.{ps1,sh}` wrap it cross-platform and
`measure-startup.ps1` now forwards to it. Deterministic regressions live in
`tests/Nuvio.Desktop.Tests/ViewModels/Phase9PerformanceTests.cs` (5,000-item poster virtualization held
during scroll, decoded-cache 128-item LRU flood, disk-cache byte-cap LRU pressure, 200-search cancellation
storm newest-wins, fake-engine start/stop no-leak), with gated real-engine teardown added to
`ExternalMpvEngineIntegrationTests` (no orphan mpv process) and `LibMpvEngineIntegrationTests` (no obvious
growth over 12 cycles). A report-only `perf` job in `ci.yml` runs the probe on Windows/macOS/Linux and uploads
`artifacts/perf/`. Tuning outcomes are evidence-driven and recorded: 9.8 no change (within budget;
byte-aware decoded cap logged as a follow-up), 9.9 defaults retained with a documented real-stream
measurement path (`NUVIO_PERF_MEDIA_URL`). Remaining open work: 9.7 still needs a real-window Avalonia
UI-thread/frame-time profiling note because virtualization bounds alone prove container behavior, not scroll
stall health. Docs:
`docs/perf/{README,perf-report-template,baseline-2026-06}.md`, expanded `docs/performance-budget.md` (measured
budgets + release gate), `docs/regression-checklist.md` (Phase 9 Gate), and `docs/architecture.md`. Verified
on Windows with `./scripts/verify-all.ps1`; macOS/Linux covered by the CI matrix running the same tests +
probe.

- **Verify**: the app stays within memory budget during common browsing/playback flows and does not show obvious leaks after repeated actions.
- **Regression**:
  - Performance: cold start under target on test machine.
  - Performance: poster grid scroll does not grow memory unbounded.
  - Performance: playback start/stop loop returns near baseline after cleanup.
  - Performance: search cancellation prevents CPU/network pileups.
  - Performance: app remains responsive while metadata/images load.
  - Cross-platform: memory probe runs on Windows, macOS, and Linux.

### Phase 9 Regression Gate

- Memory budget is measured, not guessed.
- Poster grid virtualization is proven.
- Player start/stop loop is stable.
- Cache limits work.
- Performance report is attached to release candidate.

---

## Phase 10: Release Candidate, Compliance, and Public Beta

> Freeze the first public beta candidate only after the cross-platform basics are real.

**Suggested skills:** `security-best-practices`; optional `license-compliance-auditor` and release automation skill for final gate review.

**Avalonia skill usage:** Use the `avalonia` skill for final desktop UI, accessibility, launch, packaging smoke, and regression review before beta.

**Upstream reuse:** Use `upstream/NuvioMobile` as the primary behavioral source for release checklists, legal language, support categories, and parity expectations. Use `upstream/NuvioTV` as the playback/TV UX reference for known issues, playback support categories, and final player UX parity.

- [ ] 10.1 Freeze feature scope for beta 1.
- [ ] 10.2 Create release checklist covering Windows, macOS, Linux, player, packaging, performance, and compliance.
- [ ] 10.3 Run full regression matrix on all platforms.
- [ ] 10.4 Run packaging smoke tests from clean user profile.
- [ ] 10.5 Confirm license inventory and source bundle.
- [ ] 10.6 Confirm logs redact sensitive values.
- [ ] 10.7 Confirm app does not host, store, or distribute media.
- [ ] 10.8 Write known issues per OS.
- [ ] 10.9 Write install instructions per OS.
- [ ] 10.10 Tag beta release and attach artifacts.
- [ ] 10.11 Create issue templates for playback bug, packaging bug, addon/source bug, and performance bug.
- [ ] 10.12 Add crash/log collection instructions that avoid asking users to paste sensitive stream URLs.
- [ ] 10.13 Confirm dependency license inventory has no placeholder/TBD entries.
- [ ] 10.14 Confirm beta artifacts are generated from a clean checkout and not from local ignored state.

- **Verify**: beta release artifacts are usable on Windows, macOS, and Linux, with clear known issues and rollback plan.
- **Regression**:
  - Full: all unit tests pass.
  - Full: all integration tests pass.
  - Full: player smoke tests pass with external mpv and embedded libmpv where enabled.
  - Full: packaged artifact launches on each OS.
  - Full: source bundle and dependency licenses are attached.
  - Manual: install instructions are followed successfully on each OS.

### Phase 10 Regression Gate

- Public beta artifacts are attached for all three platforms.
- License/source obligations are satisfied.
- Known issues are documented.
- Install instructions exist.
- Release is not Windows-only in practice.

---

## MVP Definition

The MVP is complete when:

- Windows, macOS, and Linux builds exist.
- The app can browse/search fixture or real validated addon content.
- The app can resolve playable stream sources from enabled addons/user-provided sources.
- Playback works through external mpv on all three OSes.
- Embedded libmpv works on at least Windows and Linux, with macOS either working or falling back cleanly with documented status.
- Watch progress persists locally.
- Poster/image caching is bounded.
- Packaging artifacts exist for all three OSes.
- Full regression suite passes.
- GPL/license/source release requirements are handled.

---

## Post-MVP Backlog

- Full embedded libmpv render API parity on every OS.
- macOS signed/notarized stable release channel.
- Windows MSIX/MSI auto-update story.
- Linux AppImage + deb/rpm distribution.
- Remote-control / 10-foot TV mode.
- Cloud sync for watch progress, if desired.
- Advanced subtitle styling and subtitle download helpers.
- Download/offline features only if legal/compliance scope is clear.
- Plugin/addon management UI polish.
- Crash reporting with privacy-safe redaction.

---

## First Week Work Plan

| Day | Work |
|---|---|
| Day 1 | Fork NuvioMobile, create `desktop-port`, add architecture docs, choose repo layout |
| Day 2 | Create .NET solution, Avalonia shell, CI matrix |
| Day 3 | Add `IPlayerEngine`, external mpv process locator, JSON command serializer |
| Day 4 | Implement IPC event loop and basic play/pause/seek against local test media |
| Day 5 | Add basic Home/Search/Details/Player views with fixture data |
| Day 6 | Add SQLite settings/progress skeleton and disk image cache design |
| Day 7 | Run Windows/macOS/Linux smoke tests and update regression checklist |

---

## Non-Negotiable Checklist

- [ ] Do not use Electron.
- [ ] Do not use Tauri or another WebView/browser-shell desktop runtime.
- [ ] Do not switch the main app from C#/.NET + Avalonia to Rust unless Avalonia fails a documented cross-platform gate.
- [ ] Do not introduce Rust except as an isolated helper or native interop module backed by Phase 6 or Phase 9 profiling evidence.
- [ ] Do not fork mpv unless a real upstream player patch is needed.
- [ ] Do not make Windows-only assumptions in paths, native libraries, or packaging.
- [ ] Do not directly port Android TV UI as desktop UI.
- [ ] Do not load unbounded catalog/poster data into memory.
- [ ] Do not log sensitive stream URLs or headers.
- [ ] Do not distribute binaries without GPL/source/license compliance.
- [ ] Do not mark a phase complete unless Windows, macOS, and Linux regression items are addressed.
