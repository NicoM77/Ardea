# Installation

> Images are published to `ghcr.io/nicom77/ardea` from the first release on (task 7.3). Until then, build
> one from source: `docker build -f deploy/Dockerfile -t ardea:dev .` and set `ARDEA_IMAGE=ardea:dev`. Such a build
> reports its version as `dev`; `--build-arg VERSION=0.1.0` gives it a version.

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
  `api.github.com` and `dgpproduction.blob.core.windows.net`. For Web Push: the browser makers' push services
  (`fcm.googleapis.com`, `updates.push.services.mozilla.com`, `web.push.apple.com`, `*.push.apple.com`,
  `*.notify.windows.com`). For a hosted language model: its address (for example `api.anthropic.com`). Everything
  else is closed to Ardea; `ardea doctor` tries the hosts of the sources, the SMTP server and the providers.
- A host name with HTTPS in front of the API. Ardea answers API calls only over HTTPS.

## Releases and image tags

Each [release](https://github.com/NicoM77/Ardea/releases) lists its changes and carries `compose.yaml`,
`compose.caddy.yaml`, `Caddyfile`, the SBOMs (CycloneDX: `ardea-dotnet.cdx.json`, `ardea-web.cdx.json`) and
`THIRD-PARTY-NOTICES.md`. The image `ghcr.io/nicom77/ardea` exists for `linux/amd64` and `linux/arm64` under the
tags `0.4.2` (exactly this version), `0.4` (its patches) and `0` (every 0.x version, the tag `compose.yaml` uses).
Versions before 1.0 are pre-releases; from 1.0 on, `latest` follows the newest release. `ardea version`, the first
line of `ardea doctor` and the administration show the version that runs.

## Verify the image

Every release image is signed with [cosign](https://docs.sigstore.dev/) without a key, by the identity of the
repository's release workflow, and has its build provenance and SBOM attested on GitHub. Check them before the first
start and after an update, with cosign 3 or later and the GitHub CLI (`gh`, signed in with any account). Set
`version` to the release you pull:

```bash
image=ghcr.io/nicom77/ardea
version=0.1.0
identity='^https://github\.com/NicoM77/Ardea/\.github/workflows/release\.yml@refs/heads/main$'
issuer=https://token.actions.githubusercontent.com
signer=https://github.com/NicoM77/Ardea/.github/workflows/release.yml@refs/heads/main

cosign verify "$image:$version" --certificate-identity-regexp "$identity" --certificate-oidc-issuer "$issuer"
gh attestation verify "oci://$image:$version" --repo NicoM77/Ardea --cert-identity "$signer" --deny-self-hosted-runners
gh attestation verify "oci://$image:$version" --repo NicoM77/Ardea --cert-identity "$signer" --deny-self-hosted-runners \
  --predicate-type https://cyclonedx.org/bom

for arch in amd64 arm64; do
  digest=$(docker buildx imagetools inspect "$image:$version" \
    --format "{{range .Manifest.Manifests}}{{if eq .Platform.Architecture \"$arch\"}}{{.Digest}}{{end}}{{end}}")
  echo "== $arch $digest"
  cosign verify "$image@$digest" --certificate-identity-regexp "$identity" --certificate-oidc-issuer "$issuer"
  gh attestation verify "oci://$image@$digest" --repo NicoM77/Ardea --cert-identity "$signer" --deny-self-hosted-runners
done
```

The first two commands check the multi-architecture index (what `docker pull` resolves the tag to), the third its
SBOM; the loop checks the image of each architecture by its own digest. Each command fails unless the image was
built and signed by `release.yml` on the main branch of `NicoM77/Ardea`, on a GitHub-hosted runner. An image tag is
set only after its image was signed and attested, so a tag that fails these checks was not made by a release. To run exactly what you checked, pin the
digest in `.env`: `ARDEA_IMAGE=ghcr.io/nicom77/ardea:0.1.0@sha256:…`.

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
   database restored without it cannot decrypt them. Keep it apart from the database dumps, too: a dump alone
   shows the technology profiles of every organisation but none of the secrets. Put the Docker volumes and the
   backups on an encrypted disk; the profiles are a map of each organisation's attack surface.

2. Start with HTTPS in front. With the bundled Caddy, which gets a certificate for a public domain itself:

   ```bash
   ARDEA_DOMAIN=ardea.example.com docker compose -f compose.yaml -f compose.caddy.yaml up -d
   ```

   With your own proxy or a tunnel instead, use `compose.yaml` alone (it publishes the API on
   `127.0.0.1:8080`) and see `ARDEA_TRUSTED_PROXIES` in [Configuration](configuration.md).

3. Open the public address. The first page asks for the **setup token** (`ARDEA_SETUP_TOKEN` in `.env.app`,
   also printed in the API log at the first start) and creates the first user, who is the instance
   administrator. Remove the token from `.env.app` afterwards.

4. In the administration: registration mode (by invitation is the default), SMTP for e-mail (with a test
   e-mail), the sources, and, if you want assessments by a language model, the providers
   ([Configuration](configuration.md), [Operation](operations.md#language-model)). Then create an organisation; its
   setup wizard starts with the stack (the products the organisation runs, coarsely) and then asks for projects and
   repositories, both optional. Web Push needs nothing more than HTTPS
   ([Notifications](operations.md#notifications)).

`docker compose ps` shows all three services healthy; `docker compose run --rm api doctor` checks the
configuration, database and schema, and warns about an SMTP server, language model provider or source it cannot
reach.

The first load of the sources starts at once and runs in the background; the *Sources* page in the administration
shows its progress. With the first load done, **the catalogue starts to learn** from the products the sources name
and the curated catalogue does not know (once a day, or with `ardea catalog grow`);
see [Operation](operations.md#catalogue-that-learns). Back up the database and the master key from the first day
([Operation](operations.md#backups)).
