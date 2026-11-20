#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
image="${1:-}"
playwright_image="mcr.microsoft.com/playwright:v1.56.1-noble"
out="$root/docs/screenshots"
export MSYS_NO_PATHCONV=1

host_path() {
  if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi
}

if [ -z "$image" ]; then
  image="ardea:screenshots"
  docker build -f "$root/deploy/Dockerfile" -t "$image" "$root"
fi

started=$(date +%s)
work="$(mktemp -d)"
project="ardea-screenshots-$$"
compose=(docker compose --progress quiet -p "$project" --project-directory "$(host_path "$work")" -f "$(host_path "$work/compose.yaml")")
cleanup() {
  if [ "${SCREENSHOTS_KEEP:-0}" = "1" ]; then
    echo "Instance kept: ${compose[*]} down -v"
    return
  fi
  "${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

cp "$root/deploy/compose.yaml" "$work/"
docker run --rm --user "$(id -u):$(id -g)" -v "$(host_path "$work"):/out" "$image" \
  init-env --dir /out --public-url http://localhost:8080 >/dev/null
export ARDEA_IMAGE="$image" ARDEA_HOST_PORT="${SCREENSHOTS_API_PORT:-18090}"

"${compose[@]}" pull --quiet db
"${compose[@]}" up -d --wait --wait-timeout 180 api

ARDEA_DB_OWNER_PASSWORD="$(sed -n 's/^ARDEA_DB_OWNER_PASSWORD=//p' "$work/.env")"
export ARDEA_DB_OWNER_PASSWORD
seed="$("${compose[@]}" run --rm -T -e ARDEA_DB_OWNER_PASSWORD api seed-demo)"
unset ARDEA_DB_OWNER_PASSWORD
SCREENSHOTS_SEEDED_AT="$(date +%s)000"
SCREENSHOTS_EMAIL="$(printf '%s\n' "$seed" | sed -n 's/^  e-mail  *//p')"
SCREENSHOTS_PASSWORD="$(printf '%s\n' "$seed" | sed -n 's/^  password  *//p')"
seed=""
[ -n "$SCREENSHOTS_EMAIL" ] && [ -n "$SCREENSHOTS_PASSWORD" ] || { echo "seed-demo printed no sign-in"; exit 1; }
export SCREENSHOTS_EMAIL SCREENSHOTS_PASSWORD SCREENSHOTS_SEEDED_AT
echo "Demo data loaded"

"${compose[@]}" up -d --wait --wait-timeout 180
api="$("${compose[@]}" ps -q api)"

mkdir -p "$out"
rm -f "$out"/*.png
docker run --rm --ipc=host --network "container:$api" --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e CI=true -e SCREENSHOTS_EMAIL -e SCREENSHOTS_PASSWORD -e SCREENSHOTS_SEEDED_AT \
  -v "$(host_path "$root"):/repo" -w /repo/tests/e2e "$playwright_image" \
  sh -c '[ -d node_modules/@playwright/test ] || npm ci --no-audit --no-fund --loglevel=error; npx playwright test --config screenshots.config.ts'

echo "Screenshots in docs/screenshots after $(( $(date +%s) - started ))s:"
ls -l "$out"/*.png | awk '{ printf "  %-28s %7.0f KB\n", $NF, $5 / 1024 }' | sed "s|$out/||"
