# Installation

> Images are published to `ghcr.io/nicom77/ardea` from the first release on (task 7.3). Until then, build
> one from source: `docker build -f deploy/Dockerfile -t ardea:dev .` and set `ARDEA_IMAGE=ardea:dev`.

Ardea runs as one image in three roles (`migrate`, `api`, `worker`) next to PostgreSQL 18, described in
[`deploy/compose.yaml`](../../deploy/compose.yaml).

## Requirements

- Docker with Compose v2, on Linux (`amd64` or `arm64`).
- For up to about 20 organisations without a local language model: 2 vCPU, 4 GB RAM, 20 GB disk. The first
  start loads 180 days of vulnerability data (about 1 to 2 GB) in under an hour on such a machine
  ([phase 1 acceptance](../acceptance/phase-1.md)).
- Outbound HTTPS to the sources: `cveawg.mitre.org`, `github.com`, `api.github.com`,
  `raw.githubusercontent.com`, `objects.githubusercontent.com` and `release-assets.githubusercontent.com` (CVE
  Program), `www.cisa.gov` (KEV), `epss.empiricalsecurity.com` (EPSS), `osv-vulnerabilities.storage.googleapis.com`
  and `api.osv.dev` (OSV), `security-hub.ncsc.admin.ch` and `www.bacs.admin.ch` (NCSC). For repositories:
  `api.github.com` and `dgpproduction.blob.core.windows.net`.
- A host name with HTTPS in front of the API. Ardea answers API calls only over HTTPS.

## Steps

1. Create a folder and generate the mandatory values. The image runs as an unprivileged user, so run the
   command as your own user:

   ```bash
   mkdir ardea && cd ardea
   curl -fsSLO https://raw.githubusercontent.com/NicoM77/Ardea/main/deploy/compose.yaml
   curl -fsSLO https://raw.githubusercontent.com/NicoM77/Ardea/main/deploy/compose.caddy.yaml
   curl -fsSLO https://raw.githubusercontent.com/NicoM77/Ardea/main/deploy/Caddyfile
   docker run --rm --user "$(id -u):$(id -g)" -v "$PWD:/out" ghcr.io/nicom77/ardea:0 init-env --dir /out --public-url https://ardea.example.com
   ```

   `.env` holds the database passwords for the database and `migrate`; `.env.app` holds what API and worker
   need. **Keep `ARDEA_MASTER_KEY` in a separate backup**: it encrypts tokens and the SMTP password, and a
   database restored without it cannot decrypt them.

2. Start with HTTPS in front. With the bundled Caddy, which gets a certificate for a public domain itself:

   ```bash
   ARDEA_DOMAIN=ardea.example.com docker compose -f compose.yaml -f compose.caddy.yaml up -d
   ```

   With your own proxy or a tunnel instead, use `compose.yaml` alone (it publishes the API on
   `127.0.0.1:8080`) and see `ARDEA_TRUSTED_PROXIES` in [Configuration](configuration.md).

3. Open the public address. The first page asks for the **setup token** (`ARDEA_SETUP_TOKEN` in `.env.app`,
   also printed in the API log at the first start) and creates the first user, who is the instance
   administrator. Remove the token from `.env.app` afterwards.

4. In the administration: registration mode (by invitation is the default), SMTP for e-mail, and the
   sources. Then create an organisation; its setup wizard walks through the first project.

`docker compose ps` shows all three services healthy; `docker compose run --rm api doctor` checks the
configuration, database, schema, SMTP and the reachability of the sources.
