# ADR 0001: Rebuild the desktop UI in Avalonia

- Status: Accepted
- Date: Phase 0 / Phase 1

## Context

Nuvio ships as mobile (`NuvioMobile`) and TV (`NuvioTV`) apps. The desktop effort
needs a Windows/macOS/Linux client with native-feeling windowing, real video
playback, and a small memory footprint. Two broad options exist:

1. Wrap the existing JavaScript/React UI in a browser/Electron-style shell.
2. Rebuild the UI natively and port only the *behavior* (addon, stream, metadata,
   progress, settings logic) into a shared core.

## Decision

Rebuild the desktop UI in **Avalonia + C#/.NET**. Playback uses **mpv/libmpv** behind
a single `IPlayerEngine`; durable state uses **SQLite** with hand-written migrations.

**Electron / browser-runtime shells are explicitly rejected** and this is a hard,
ongoing constraint, not a transitional choice. The upstream React UI is treated as a
behavioral reference only; UI is re-expressed in Avalonia XAML + MVVM, and the
portable logic is ported deliberately into `Nuvio.Core` with tests.

## Rationale

- **Memory / startup budget.** A Chromium runtime contradicts the performance budget
  (idle under ~200 MB, startup under ~2.5 s). A native toolkit gets far closer.
- **Real playback.** mpv/libmpv integration (child process JSON IPC and in-process
  P/Invoke) is cleaner from .NET than from inside a browser sandbox.
- **One portable core, isolated platform seams.** C# lets `Nuvio.Core` stay portable
  while `Nuvio.Platform` owns OS specifics (paths, native loading, packaging facts).
- **Single language across UI, services, persistence, and interop** reduces the
  context-switching and bridge code an Electron split would impose.

## Consequences

- The desktop UI does not share code with the React apps; parity is maintained through
  behavior-mapping docs (`phase-1-behavior-mapping.md`, `player-parity-notes.md`) and
  tests rather than shared components.
- Packaging is OS-native (portable zip / `.app` + DMG / tar.gz) rather than an Electron
  builder pipeline. See `docs/packaging.md`.
- Avalonia/.NET and mpv are the load-bearing third-party dependencies and must stay
  current in the dependency-license inventory and GPL compliance docs.
