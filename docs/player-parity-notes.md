# Player Parity Notes

Playback UX notes captured from `NuvioTV` (and `NuvioMobile`) to guide the desktop
player across later phases. These are behavioral references; the desktop player is
rebuilt natively behind a single `IPlayerEngine`. See `docs/player-integration.md` for
the implemented engine contract and `docs/architecture.md` for layering rules.

## Engine model

- All playback goes through `IPlayerEngine`. UI code must never call mpv/libmpv directly.
- Two implementations emit identical `PlayerEvent` records so consumers are engine-agnostic:
  - `ExternalMpvEngine` — child `mpv` process over JSON IPC. **Default and guaranteed fallback.**
  - `LibMpvEngine` — in-process libmpv via P/Invoke (experimental spike).
- `SelectingPlayerEngineFactory` chooses libmpv only when the user opts in *and* the
  native library loads; otherwise external mpv. Runtime failover retries on external mpv
  within the same session if libmpv fails at playback time.

## Controls / interaction parity

- Core transport: play/pause, seek backward/forward, stop, volume, fullscreen,
  audio-track selection, subtitle-track selection — all routed through `IPlayerEngine`.
- Observed state mapped to events: pause, position, duration, file-loaded, end-file,
  buffering/cache, and track-list updates.
- Fullscreen is promoted to the window level (F11), hiding chrome; Esc exits fullscreen
  before returning to browsing.
- A mini-player mode reuses the same control bar and keeps the single active engine alive.

## Dependency / packaging implications

- No mpv/libmpv binary is bundled. The app relies on the discovery chain documented in
  `docs/native-dependency-provenance.md` and `docs/mpv-setup.md`.
- Player diagnostics surface which engine is active and the mpv/libmpv discovery result
  (found/not-found, path, version, source) so the dependency story is clear to users.

## Redaction parity

Player logs redact stream URLs, query strings, and sensitive headers before IPC command
logs or load failures are surfaced.
