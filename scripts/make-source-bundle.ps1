<#
.SYNOPSIS
  Produce a reproducible GPL source tarball from HEAD.
.DESCRIPTION
  Uses `git archive`, so the tarball honors .gitignore: the git-ignored upstream/
  reference checkouts and build outputs are excluded automatically. The archive is
  deterministic for a given commit, which keeps releases reproducible.
.PARAMETER Version
  Release version. Defaults to <VersionPrefix> in Directory.Build.props.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repoRoot
try {
    if (-not $Version) {
        $props = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw
        if ($props -match '<VersionPrefix>([^<]+)</VersionPrefix>') {
            $Version = $Matches[1]
        }
    }
    if (-not $Version) {
        throw 'make-source-bundle: could not determine version.'
    }

    $prefix = "nuvio-desktop-$Version/"
    $outDir = Join-Path $repoRoot 'artifacts/source'
    $out = Join-Path $outDir "nuvio-desktop-src-$Version.tar.gz"

    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    & git archive --format=tar.gz --prefix=$prefix -o $out HEAD
    if ($LASTEXITCODE -ne 0) { throw "git archive failed with exit code $LASTEXITCODE" }

    Write-Host "Wrote $out"

    $entries = & tar -tzf $out
    if ($entries | Where-Object { $_ -like "${prefix}upstream/*" }) {
        throw 'make-source-bundle: upstream/ leaked into the source bundle.'
    }
    Write-Host 'Verified: upstream/ is excluded.'
}
finally {
    Pop-Location
}
