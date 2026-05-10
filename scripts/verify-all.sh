#!/usr/bin/env bash
set -euo pipefail

dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build

for path in LICENSE NOTICE docs/legal-compliance.md docs/dependency-licenses.md; do
  test -f "$path"
done
