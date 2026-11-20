#!/usr/bin/env bash
set -euo pipefail
image="${1:?usage: tests/e2e/run.sh <image>}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
here="$root/tests/e2e"
playwright_image="mcr.microsoft.com/playwright:v1.56.1-noble"
https_port="${E2E_HTTPS_PORT:-443}"
http_port="${E2E_HTTP_PORT:-80}"
base_url="https://localhost"
[ "$https_port" = "443" ] || base_url="https://localhost:$https_port"

work="$(mktemp -d)"
project="ardea-e2e-$$"
compose=(docker compose -p "$project" --project-directory "$work" -f "$work/compose.yaml" -f "$work/compose.caddy.yaml")
cleanup() {
  if [ "${E2E_KEEP:-0}" = "1" ]; then
    echo "Instance kept at $base_url: ${compose[*]} down -v"
    return
  fi
  "${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

cp "$root/deploy/compose.yaml" "$root/deploy/compose.caddy.yaml" "$root/deploy/Caddyfile" "$work/"
if [ -n "${E2E_WEB_DIST:-}" ]; then
  web_dist="$(cd "$E2E_WEB_DIST" && pwd)"
  printf 'services:\n  api:\n    volumes: ["%s:/app/wwwroot:ro"]\n' "$web_dist" > "$work/compose.web.yaml"
  compose+=(-f "$work/compose.web.yaml")
fi
docker run --rm --user "$(id -u):$(id -g)" -v "$work:/out" "$image" init-env --dir /out --public-url "$base_url" >/dev/null
echo "ARDEA_RATE_LIMIT__AUTH_PER_MINUTE=120" >> "$work/.env.app"
setup_token="$(sed -n 's/^ARDEA_SETUP_TOKEN=//p' "$work/.env.app")"
[ -n "$setup_token" ] || { echo "::error::No setup token in .env.app"; exit 1; }

export ARDEA_IMAGE="$image" ARDEA_DOMAIN=localhost ARDEA_HOST_PORT="${E2E_API_PORT:-18081}"
export ARDEA_HTTP_PORT_PUBLIC="$http_port" ARDEA_HTTPS_PORT_PUBLIC="$https_port"

"${compose[@]}" pull --quiet db caddy

start=$(date +%s)
"${compose[@]}" up -d --wait --wait-timeout 180
elapsed=$(( $(date +%s) - start ))
echo "Instance healthy after ${elapsed}s"
if (( elapsed > 180 )); then
  echo "::error::Instance took ${elapsed}s to become healthy (limit 180s)"
  exit 1
fi

for _ in $(seq 1 30); do
  curl -ksf -o /dev/null "$base_url/" && break
  sleep 1
done
curl -ksf -o /dev/null "$base_url/" || { echo "::error::$base_url does not answer"; exit 1; }

docker run --rm --ipc=host --network host --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e CI="${CI:-}" -e E2E_BASE_URL="$base_url" -e E2E_SETUP_TOKEN="$setup_token" \
  -v "$here:/e2e" -w /e2e "$playwright_image" npx playwright test
