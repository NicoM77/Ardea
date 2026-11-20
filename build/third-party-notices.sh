#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
out="${1:-artifacts/THIRD-PARTY-NOTICES.md}"
mkdir -p "$(dirname "$out")"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

dotnet tool restore >/dev/null
dotnet nuget-license \
  --input src/Hosts/Ardea.Host/Ardea.Host.csproj \
  --include-transitive \
  --override-package-information build/licenses/overrides.json \
  --output Markdown > "$tmp/nuget.md"

if [ -n "${NPM_NOTICES:-}" ]; then
  cp "$NPM_NOTICES" "$tmp/npm.md"
elif [ -d web/node_modules ]; then
  (cd web && node scripts/third-party-notices.mjs "$tmp/npm.md")
else
  echo "web/node_modules missing: run 'npm ci' in web/ first." >&2
  exit 1
fi

{
  echo "# Third-party notices"
  echo
  echo "Ardea itself is published under the licence in LICENSE. It ships with the components below,"
  echo "each under its own licence."
  echo
  echo "## .NET (NuGet, including transitive packages)"
  echo
  cat "$tmp/nuget.md"
  echo
  cat "$tmp/npm.md"
} > "$out"
echo "Wrote $out"
