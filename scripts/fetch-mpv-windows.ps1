Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host 'Nuvio does not bundle mpv in Phase 2.'
Write-Host 'Install mpv directly from https://mpv.io/installation/ or use MPV Manager as an optional setup helper.'
Write-Host 'Nuvio must be pointed at the final mpv.exe, not mpv-manager.exe.'
Write-Host 'Set NUVIO_MPV_PATH to the full mpv.exe path if automatic discovery does not find it.'
