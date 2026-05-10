Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repos = @(
    @{ Name = 'NuvioMobile'; Url = 'https://github.com/NuvioMedia/NuvioMobile.git' },
    @{ Name = 'NuvioTV'; Url = 'https://github.com/NuvioMedia/NuvioTV.git' }
)

New-Item -ItemType Directory -Force -Path upstream | Out-Null

foreach ($repo in $repos) {
    $path = Join-Path 'upstream' $repo.Name
    if (Test-Path (Join-Path $path '.git')) {
        git -C $path fetch --depth 1 origin
        git -C $path reset --hard FETCH_HEAD
        continue
    }

    git clone --depth 1 $repo.Url $path
}
