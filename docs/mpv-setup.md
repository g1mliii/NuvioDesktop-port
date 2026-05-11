# mpv Setup

Nuvio launches the real `mpv` binary directly through JSON IPC. MPV Manager can be used as an optional setup helper, but it is not a runtime dependency and Nuvio does not launch or control MPV Manager.

## Discovery Order

1. `NUVIO_MPV_PATH`, when it points to a real `mpv` executable.
2. A future Nuvio-managed or bundled `mpv` path beside the app.
3. `mpv` installed by MPV Manager, when the final `mpv` binary is discoverable.
4. `mpv` on `PATH`.
5. Common platform install locations.

The locator rejects `mpv-manager` executables. A manager binary may install or configure mpv, but playback must still use `mpv.exe` on Windows or `mpv` on macOS/Linux.

## Direct Install

- Windows: use the official mpv install page and a current Windows build, or install through a trusted package manager.
- macOS: use Homebrew, MacPorts, or the official mpv install page.
- Linux: use a current distro package, third-party package, or mpv-build when the distro package is too old.

After installing, run:

```powershell
mpv --version
```

If Nuvio cannot find mpv, set `NUVIO_MPV_PATH` to the actual player executable.

## MPV Manager

MPV Manager is useful for normal users because it can install and update mpv, manage configs, select hardware acceleration, and provide Web UI/TUI setup flows. Nuvio treats it as setup-only:

- Do not ship MPV Manager with Nuvio without a later security and licensing review.
- Do not call MPV Manager from app startup or playback.
- Do not depend on MPV Manager's Web UI, TUI, self-update behavior, or config mutation.
- Do not accept an unsigned `mpv-manager` binary as the playback executable.

When MPV Manager is used, Nuvio should detect or be pointed to the final `mpv` binary that the manager installed.

## Nuvio Playback Defaults

Nuvio owns the defaults for Nuvio-launched playback instead of relying on a user's global `mpv.conf`:

```text
vo=gpu-next
gpu-api=auto
hwdec=auto-safe
cache=yes
demuxer-max-bytes=150M
demuxer-max-back-bytes=50M
save-position-on-quit=no
config=no
```

These defaults keep the high-quality mpv path available while avoiding surprises from user-level config during app playback.

## Test Media Policy

Playback tests must use generated or clearly redistributable local media. Do not commit private stream URLs, provider tokens, copyrighted samples, or personal files. Real stream smoke checks can be run manually, but they must stay out of fixtures, logs, and source control.

## Running Integration Checks

The default verification scripts run the unit and headless UI suite without requiring mpv on a developer machine:

```powershell
./scripts/verify-all.ps1
```

When `mpv` is installed, enable the real IPC and generated-fixture playback checks:

```powershell
$env:NUVIO_RUN_MPV_INTEGRATION='1'
dotnet test --configuration Release
```

CI installs mpv on Windows, macOS, and Linux and runs with `NUVIO_RUN_MPV_INTEGRATION=1`.

## References

- mpv install notes: https://mpv.io/installation/
- mpv JSON IPC/manual: https://mpv.io/manual/stable/
- MPV Manager feature page: https://mpv.rocks/manager/
- MPV Manager repository: https://gitgud.io/mike/mpv-manager
