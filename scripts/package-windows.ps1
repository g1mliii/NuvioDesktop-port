<#
.SYNOPSIS
  Publish, stage compliance, smoke, and produce the portable Windows zip artifact.
.PARAMETER Rid
  Runtime identifier. Defaults to win-x64; pass win-arm64 to reuse the same logic.
.PARAMETER Version
  Release version. Defaults to <VersionPrefix> in Directory.Build.props.
#>
[CmdletBinding()]
param(
    [string]$Rid = 'win-x64',
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repoRoot
try {
    if (-not $Version) {
        $props = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw
        if ($props -match '<VersionPrefix>([^<]+)</VersionPrefix>') { $Version = $Matches[1] }
    }
    if (-not $Version) { throw 'package-windows: could not determine version.' }

    $project = Join-Path $repoRoot 'src/Nuvio.Desktop/Nuvio.Desktop.csproj'
    $publishDir = Join-Path $repoRoot "artifacts/publish/$Rid"
    $distDir = Join-Path $repoRoot 'artifacts/dist'
    $artifact = Join-Path $distDir "nuvio-desktop-$Version-$Rid.zip"

    Write-Host "==> Publishing $Rid (version $Version)"
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    & dotnet publish $project -c Release -r $Rid --self-contained true -p:Version=$Version -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    Write-Host '==> Staging compliance files'
    & (Join-Path $PSScriptRoot 'lib/stage-compliance.ps1') -TargetDir $publishDir -RepoRoot $repoRoot

    Write-Host '==> Launch/exit smoke (--self-check)'
    $hostRid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
    $onWindows = -not (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) -or $IsWindows
    if ($onWindows -and $Rid -eq $hostRid) {
        & (Join-Path $publishDir 'Nuvio.Desktop.exe') --self-check
        if ($LASTEXITCODE -ne 0) { throw "self-check failed ($LASTEXITCODE)" }
    }
    else {
        Write-Host "    skipped: not running on a matching Windows host ($Rid)"
    }

    Write-Host '==> Creating zip'
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    if (Test-Path $artifact) { Remove-Item -Force $artifact }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $artifact

    Write-Host "Wrote $artifact"
}
finally {
    Pop-Location
}
