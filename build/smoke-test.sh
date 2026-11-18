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
# SMOKE_KEEP=1 leaves the instance running for a manual look; stop it with the printed command.
if [ "${SMOKE_KEEP:-0}" = "1" ]; then
  trap 'echo "Instance kept: docker compose -p $project --project-directory $work down -v"' EXIT
else
  trap cleanup EXIT
fi

cp "$root/deploy/compose.yaml" "$work/"
docker run --rm --user "$(id -u):$(id -g)" -v "$work:/out" "$image" init-env --dir /out --public-url https://ardea.test >/dev/null
export ARDEA_IMAGE="$image" ARDEA_HOST_PORT="${ARDEA_HOST_PORT:-18080}"

start=$(date +%s)
docker compose -p "$project" --project-directory "$work" -f "$work/compose.yaml" up -d --wait --wait-timeout 180
elapsed=$(( $(date +%s) - start ))
echo "Instance healthy after ${elapsed}s"

curl -fsS "http://127.0.0.1:${ARDEA_HOST_PORT}/health/ready"; echo
# The API serves the web app, falls back to it for client routes, and keeps unknown API paths a 404.
curl -fsS "http://127.0.0.1:${ARDEA_HOST_PORT}/" | grep -q "<ardea-root" || { echo "::error::Web app not served at /"; exit 1; }
curl -fsS "http://127.0.0.1:${ARDEA_HOST_PORT}/sign-in" | grep -q "<ardea-root" || { echo "::error::Client route /sign-in not served"; exit 1; }
# API calls arrive through the HTTPS proxy, which the curl calls stand in for (Docker's gateway is a trusted proxy).
proxy=(-H "X-Forwarded-Proto: https")
status=$(curl -s -o /dev/null -w '%{http_code}' "${proxy[@]}" "http://127.0.0.1:${ARDEA_HOST_PORT}/api/v1/does-not-exist")
[ "$status" = "404" ] || { echo "::error::Unknown API path answered $status"; exit 1; }
curl -fsS "${proxy[@]}" "http://127.0.0.1:${ARDEA_HOST_PORT}/api/v1/setup/status"; echo
status=$(curl -s -o /dev/null -w '%{http_code}' "${proxy[@]}" "http://127.0.0.1:${ARDEA_HOST_PORT}/api/v1/auth/session")
[ "$status" = "401" ] || { echo "::error::Session endpoint answered $status instead of 401 for a visitor"; exit 1; }
status=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:${ARDEA_HOST_PORT}/api/v1/setup/status")
[ "$status" = "403" ] || { echo "::error::Plain HTTP API call answered $status instead of 403 https_required"; exit 1; }
docker compose -p "$project" --project-directory "$work" -f "$work/compose.yaml" exec -T api /app/ardea doctor
docker compose -p "$project" --project-directory "$work" -f "$work/compose.yaml" exec -T worker /app/ardea doctor

# A second migrate on a database with data writes a dump first only when migrations are pending;
# with none pending it is a no-op.
docker compose -p "$project" --project-directory "$work" -f "$work/compose.yaml" run --rm migrate >/dev/null

if (( elapsed > 180 )); then
  echo "::error::Instance took ${elapsed}s to become healthy (limit 180s)"
  exit 1
fi
