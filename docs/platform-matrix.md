# Platform Matrix

| Platform | MVP Target | Runtime IDs | First Artifact |
|---|---|---|---|
| Windows | Windows 10 22H2+ / Windows 11 | `win-x64`, later `win-arm64` | portable zip |
| macOS | macOS 14+, macOS 13 best effort | `osx-arm64`, `osx-x64` | `.app` bundle + DMG |
| Linux | Ubuntu/Debian/Fedora, X11 first | `linux-x64`, later `linux-arm64` | tar.gz, then AppImage |

No feature is complete if it only works on one platform. CI must continue to build and test on Windows, macOS, and Linux.

## mpv Setup

All platforms use direct external `mpv` during Phase 2. MPV Manager may be documented as an optional setup helper, but Nuvio must detect and launch the final `mpv` binary directly.

| Platform | Direct mpv path examples | Optional setup helper |
|---|---|---|
| Windows | `NUVIO_MPV_PATH`, `PATH`, `C:\Program Files\mpv\mpv.exe`, `%LOCALAPPDATA%\Programs\mpv\mpv.exe`, Chocolatey `mpvio.install` `mpv.exe` | MPV Manager-installed `mpv.exe`, not `mpv-manager.exe` |
| macOS | `NUVIO_MPV_PATH`, `/opt/homebrew/bin/mpv`, `/usr/local/bin/mpv`, `/Applications/mpv.app/Contents/MacOS/mpv` | MPV Manager-installed `mpv` |
| Linux | `NUVIO_MPV_PATH`, `/usr/bin/mpv`, `/usr/local/bin/mpv`, `/snap/bin/mpv` | MPV Manager-installed `mpv` |
