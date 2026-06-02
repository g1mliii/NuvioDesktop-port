<#
.SYNOPSIS
  Copy the release-required legal/compliance files into a target directory.
.DESCRIPTION
  Shared by all per-OS packagers so the staged file set stays identical across platforms.
.PARAMETER TargetDir
  Directory to stage the compliance files into (created if missing).
.PARAMETER RepoRoot
  Repository root. Defaults to two levels above this script.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$TargetDir,

    [Parameter(Position = 1)]
    [string]$RepoRoot = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Keep this list in sync with:
#   - the <None Include> copy block in src/Nuvio.Desktop/Nuvio.Desktop.csproj
#   - the artifact-contents assertions in tests/Nuvio.Desktop.Tests
#   - the release.yml artifact-contents gate
$complianceFiles = @(
    'LICENSE',
    'NOTICE',
    'docs/dependency-licenses.md',
    'docs/source-availability.md',
    'docs/native-dependency-provenance.md'
)

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
foreach ($rel in $complianceFiles) {
    $src = Join-Path $RepoRoot $rel
    if (-not (Test-Path -LiteralPath $src -PathType Leaf)) {
        throw "stage-compliance: required file missing: $src"
    }
    Copy-Item -LiteralPath $src -Destination (Join-Path $TargetDir (Split-Path $rel -Leaf)) -Force
}

Write-Host "Staged $($complianceFiles.Count) compliance files into $TargetDir"
