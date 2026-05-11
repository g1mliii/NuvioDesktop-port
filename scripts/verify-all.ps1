Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build

$required = @(
    'LICENSE',
    'NOTICE',
    'docs/legal-compliance.md',
    'docs/dependency-licenses.md',
    'docs/architecture.md',
    'docs/platform-matrix.md',
    'docs/player-integration.md',
    'docs/performance-budget.md',
    'docs/regression-checklist.md',
    'docs/phase-1-behavior-mapping.md',
    'docs/player-parity-notes.md',
    'docs/adr/0001-rebuild-desktop-ui-in-avalonia.md'
)
foreach ($path in $required) {
    if (-not (Test-Path -Path $path -PathType Leaf)) {
        throw "Missing required handoff file: $path"
    }
}
