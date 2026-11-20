#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
project="${1:?usage: build/coverage.sh <TestProject> [<include filter> ...]}"
shift
filters=("$@")
[ ${#filters[@]} -eq 0 ] && filters=('[Ardea.*]*' '[ardea]*')
includes=()
for filter in "${filters[@]}"; do includes+=(--include "$filter"); done

dotnet tool restore >/dev/null
dotnet build "tests/$project" -v q -nologo
bin="tests/$project/bin/Debug/net10.0"
mkdir -p artifacts/coverage
dotnet coverlet "$bin/$project.dll" --target dotnet --targetargs "exec $bin/$project.dll" \
  "${includes[@]}" --exclude '[Ardea.*.Tests]*' --exclude '[Ardea.TestSupport]*' \
  --exclude-by-file '**/Migrations/*.cs' --exclude-by-file '**/obj/**' --exclude-by-attribute GeneratedCode \
  --format cobertura --output "artifacts/coverage/$project.cobertura.xml"
