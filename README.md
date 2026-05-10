# Nuvio Desktop

Nuvio Desktop is a cross-platform desktop port of Nuvio for Windows, macOS, and Linux. The implementation plan chooses Avalonia + C#/.NET for the app shell, SQLite for local state, and mpv/libmpv for playback.

This repository is currently in Phase 0: repo setup, architecture skeleton, CI, compliance gates, and an empty Avalonia desktop shell.

## Requirements

- .NET SDK 10.0.x
- Avalonia templates for local project generation
- mpv for later playback work, initially discovered as an external process

## Quick Start

```powershell
./scripts/bootstrap.ps1
./scripts/verify-all.ps1
dotnet run --project src/Nuvio.Desktop/Nuvio.Desktop.csproj
```

On macOS/Linux:

```bash
./scripts/bootstrap.sh
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

## Non-Negotiables

- No Electron or browser-runtime shell.
- Windows, macOS, and Linux are first-class targets.
- Keep playback behind `IPlayerEngine`.
- Keep platform-specific code isolated.
- Do not load full catalogs or unbounded poster images into memory.
- Do not log sensitive stream URLs, tokens, or headers.
- Do not distribute binaries without GPL/source/license compliance.
