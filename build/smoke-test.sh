#!/usr/bin/env bash
# Starts a complete instance from deploy/compose.yaml with a given image and checks that it comes
# up healthy (acceptance of phase 0: instance in under 3 minutes after the images are loaded).
# Usage: build/smoke-test.sh <image>
set -euo pipefail
image="${1:?image}"
root="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
project="ardea-smoke-$$"
cleanup() {
  docker compose -p "$project" --project-directory "$work" -f "$work/compose.yaml" down -v --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

cp "$root/deploy/compose.yaml" "$work/"
docker run --rm --user "$(id -u):$(id -g)" -v "$work:/out" "$image" init-env --dir /out --public-url https://ardea.test >/dev/null
export ARDEA_IMAGE="$image" ARDEA_HOST_PORT="${ARDEA_HOST_PORT:-18080}"

start=$(date +%s)
docker compose -p "$project" --project-directory "$work" -f "$work/compose.yaml" up -d --wait --wait-timeout 180
elapsed=$(( $(date +%s) - start ))
echo "Instance healthy after ${elapsed}s"

curl -fsS "http://127.0.0.1:${ARDEA_HOST_PORT}/health/ready"; echo
docker compose -p "$project" --project-directory "$work" -f "$work/compose.yaml" exec -T api /app/ardea doctor
docker compose -p "$project" --project-directory "$work" -f "$work/compose.yaml" exec -T worker /app/ardea doctor

# A second migrate on a database with data writes a dump first only when migrations are pending;
# with none pending it is a no-op.
docker compose -p "$project" --project-directory "$work" -f "$work/compose.yaml" run --rm migrate >/dev/null

if (( elapsed > 180 )); then
  echo "::error::Instance took ${elapsed}s to become healthy (limit 180s)"
  exit 1
fi
