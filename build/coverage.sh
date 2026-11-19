#!/usr/bin/env bash
# Measures line and branch coverage of one test project (task 7.10). There is no quota and no gate:
# the numbers show where a test may be missing, they decide nothing (master plan 13.6).
# Uses coverlet.console from the local tool manifest (.config/dotnet-tools.json, MIT).
#
# Usage: build/coverage.sh <TestProject> [<include filter> ...]
#   build/coverage.sh Ardea.Catalog.Tests '[Ardea.Catalog]*'
#   build/coverage.sh Ardea.Iam.Tests                      # every Ardea assembly the tests load, host (ardea) included
# Output: a summary per module on the console and artifacts/coverage/<TestProject>.cobertura.xml,
# which IDEs and report tools read for the uncovered lines.
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
