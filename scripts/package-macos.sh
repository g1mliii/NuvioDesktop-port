#!/usr/bin/env bash
set -euo pipefail

dotnet publish src/Nuvio.Desktop/Nuvio.Desktop.csproj --configuration Release --runtime osx-arm64 --self-contained true --output artifacts/macos/osx-arm64
