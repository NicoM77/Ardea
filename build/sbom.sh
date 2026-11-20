#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
out="${1:-artifacts/sbom}"
mkdir -p "$out"

dotnet tool restore >/dev/null
dotnet dotnet-CycloneDX src/Hosts/Ardea.Host/Ardea.Host.csproj \
  --output "$out" --filename ardea-dotnet.cdx.json --output-format Json \
  --recursive --exclude-test-projects --set-version "${VERSION:-0.0.0}"

(cd web && npx --no-install cyclonedx-npm --omit dev --output-format JSON \
  --output-file "../$out/ardea-web.cdx.json" package.json)
echo "SBOM written to $out"
