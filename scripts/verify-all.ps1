Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build

$required = @('LICENSE', 'NOTICE', 'docs/legal-compliance.md', 'docs/dependency-licenses.md')
foreach ($path in $required) {
    if (-not (Test-Path $path)) {
        throw "Missing required compliance file: $path"
    }
}
