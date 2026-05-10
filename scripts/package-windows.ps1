Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

dotnet publish src/Nuvio.Desktop/Nuvio.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/windows/win-x64
