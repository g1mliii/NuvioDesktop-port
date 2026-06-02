# Linux packaging

`scripts/package-linux.sh` publishes a self-contained build and produces the first-tier
portable artifact:

```
artifacts/dist/nuvio-desktop-<version>-linux-x64.tar.gz
```

Defaults to `linux-x64`; pass an alternate RID (e.g. `linux-arm64`) as the first
argument. The tarball root contains the `Nuvio.Desktop` binary, all runtime/native
assets, and the staged compliance files (LICENSE, NOTICE, dependency-licenses.md,
source-availability.md, native-dependency-provenance.md). The script runs
`./Nuvio.Desktop --self-check` as a launch/exit smoke when the RID matches the host.

mpv is **not** bundled; install it from your distro (`apt install mpv`,
`dnf install mpv`, …) or drop a binary into `mpv/` next to `Nuvio.Desktop`. See
`docs/native-dependency-provenance.md`.

## Deferred to the post-MVP backlog

- AppImage (plan item 8.8).
- deb / rpm packages (plan item 8.9).

See `docs/packaging.md`.
