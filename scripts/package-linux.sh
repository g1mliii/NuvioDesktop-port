#!/usr/bin/env bash
set -euo pipefail

dotnet publish src/Nuvio.Desktop/Nuvio.Desktop.csproj --configuration Release --runtime linux-x64 --self-contained true --output artifacts/linux/linux-x64
