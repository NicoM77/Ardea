#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet tool restore >/dev/null
dotnet nuget-license \
  --input Ardea.slnx \
  --include-transitive \
  --allowed-license-types build/licenses/allowed-licenses.json \
  --override-package-information build/licenses/overrides.json \
  --output Markdown > artifacts/nuget-licenses.md || {
    cat artifacts/nuget-licenses.md
    echo "::error::A NuGet dependency has a licence outside the allow-list (build/licenses/allowed-licenses.json)."
    exit 1
  }
echo "NuGet licences: all on the allow-list."
