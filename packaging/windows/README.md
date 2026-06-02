# Windows packaging

`scripts/package-windows.ps1` publishes a self-contained build and produces the
first-tier portable artifact:

```
artifacts/dist/nuvio-desktop-<version>-win-x64.zip
```

Defaults to `win-x64`; pass `-Rid win-arm64` to reuse the same logic for ARM64. The zip
root contains `Nuvio.Desktop.exe`, all runtime/native assets, and the staged compliance
files (LICENSE, NOTICE, dependency-licenses.md, source-availability.md,
native-dependency-provenance.md). The script runs `Nuvio.Desktop.exe --self-check` as a
launch/exit smoke when the published RID matches the host architecture.

## Deferred to the post-MVP backlog

- MSIX / MSI installers (plan item 8.4).
- Authenticode code signing.

Until those land, the portable zip is unsigned; SmartScreen may warn on first run. See
`docs/packaging.md`.
