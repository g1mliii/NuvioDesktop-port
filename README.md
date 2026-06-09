# Nuvio Desktop

> **Status: archived / paused — June 2026.**
> This is a cross-platform (Windows/macOS/Linux) **Avalonia + C#/.NET** port of Nuvio built around **mpv/libmpv** playback. In June 2026 the official team shipped [**NuvioMedia/NuvioDesktop**](https://github.com/NuvioMedia/NuvioDesktop) — a native (Kotlin/Compose Multiplatform) cross-platform desktop app with libmpv playback — which occupies the same niche this port targeted. Rather than maintain a parallel client against upstream, this repository is parked as a **.NET/Avalonia architecture reference**, not a maintained product. **New users should use the official app.**

Nuvio Desktop is a cross-platform desktop port of Nuvio for Windows, macOS, and Linux. The implementation plan chooses Avalonia + C#/.NET for the app shell, SQLite for local state, and mpv/libmpv for playback.

Development reached roughly **Phase 9** (external + embedded mpv behind `IPlayerEngine`, SQLite storage/cache, packaging, and performance hardening) with **Phase 11** home/visual-parity work in progress when it was paused. The full phase breakdown, a June 2026 competitor-parity audit, and the remaining backlog (Phases 11–13) live in `nuvio_desktop_cross_platform_implementation_plan.md`.

## Requirements

- .NET SDK 10.0.x
- Avalonia templates for local project generation
- mpv for playback work, initially discovered as an external process

## Quick Start

```powershell
./scripts/bootstrap.ps1
./scripts/fetch-upstream.ps1
./scripts/verify-all.ps1
dotnet run --project src/Nuvio.Desktop/Nuvio.Desktop.csproj
```

For Phase 2 playback setup, install `mpv` directly or use MPV Manager as an optional helper, then confirm the final `mpv` binary is visible to Nuvio. See `docs/mpv-setup.md`.

On macOS/Linux:

```bash
./scripts/bootstrap.sh
./scripts/fetch-upstream.sh
./scripts/verify-all.sh
dotnet run --project src/Nuvio.Desktop/Nuvio.Desktop.csproj
```

## Project Layout

- `src/Nuvio.Desktop`: Avalonia desktop UI shell.
- `src/Nuvio.Core`: shared models, validation, and app behavior contracts.
- `src/Nuvio.Player`: playback abstraction and future mpv/libmpv engines.
- `src/Nuvio.Platform`: platform detection, paths, and native integration seams.
- `src/Nuvio.Data`: SQLite/cache persistence layer.
- `docs`: architecture, player, packaging, performance, and compliance notes.
- `tests`: unit and compliance tests.

## Upstream References

`NuvioMobile` and `NuvioTV` are cloned into ignored `upstream/` folders for porting reference. Keep upstream checkouts out of Git; port behavior deliberately into the C# projects with tests.

## Non-Negotiables

- No Electron or browser-runtime shell.
- Windows, macOS, and Linux are first-class targets.
- Keep playback behind `IPlayerEngine`.
- Keep platform-specific code isolated.
- Treat MPV Manager as setup-only; launch the real `mpv` binary directly.
- Do not load full catalogs or unbounded poster images into memory.
- Do not log sensitive stream URLs, tokens, or headers.
- Do not distribute binaries without GPL/source/license compliance.
