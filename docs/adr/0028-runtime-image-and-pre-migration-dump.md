# ADR 028: Runtime image and dump before migration (experiment 0.7)

- Status: accepted
- Date: 2026-11-18
- Refines: ADR 002 (one image, three roles), ADR 024 (dump before a migration by the database server)
- Master plan: 2.8, 12.2, 12.3, task 0.7

## Context

The plan chose `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra` as the runtime image: no shell,
no package manager, non-root, with time zone data and ICU according to its description. Four points were
unconfirmed: whether time zones and cultures really work in it, how health checks run without a shell or
`curl`, how `migrate` can take a dump although the image has no `pg_dump`, and where the worker keeps large
archives when its root file system is read-only.

## Experiment and result

| Question | Result | Evidence |
|---|---|---|
| Time zones | `Europe/Zurich` resolves with daylight saving (UTC+2 on 1 July 2026). | `ardea doctor` in the image, CI smoke test |
| Culture / ICU | `de-CH` formats `1234.5` as `1’234.5`; ICU data is complete. | same |
| Health check without shell | The program checks itself: `ardea healthcheck api` calls `/health/ready` on 127.0.0.1, `ardea healthcheck worker` reads the worker's heartbeat (younger than two minutes) from the database. `docker compose up --wait` reaches healthy in about 10 s. | `build/smoke-test.sh` |
| No shell in the image | `docker run --entrypoint /bin/sh` fails; there is nothing to exec into. | manual check |
| Dump before migration | The database server writes it: `COPY (SELECT 1) TO PROGRAM 'pg_dump --format=custom …'` into `/var/lib/postgresql/backup`, newest three kept. Needs a role that may run server programs, which the bundled database's owner is (the image's superuser). A database without that right skips the dump with a warning. `pg_restore --list` reads the result. | `ServerSideBackupWriterTests` (PostgreSQL 18.7) |
| Working volume | A named volume takes over the owner of the directory it is mounted on. The image therefore ships an empty `/scratch` owned by the app user; the worker mounts its `scratch` volume there and can write although the root file system is read-only. `doctor` checks it where it is mounted. | smoke test (`/scratch writable`) |
| arm64 without emulation | The SDK stage runs on the build platform and publishes for the target architecture; the runtime stage only copies files. The arm64 image contains an AArch64 executable and builds in about 20 s. | local `buildx --platform linux/arm64`, CI on `main` |
| Image size | 170 MB (amd64). | `docker images` |

Two side findings:

- Npgsql probes for Kerberos libraries (`libgssapi_krb5.so.2`), which the chiseled image does not contain. The
  connection string sets `GssEncryptionMode=Disable`, which removes the probe and its log noise.
- Concurrent `migrate` runs could race on `CREATE ROLE`, because roles are global to the server. `migrate` now
  holds an advisory lock and retries a duplicate role as `ALTER ROLE`.

## Decision

Keep `aspnet:10.0-noble-chiseled-extra`, health checks through the program, the server-side dump (ADR 024 is
confirmed) and `/scratch` as the worker's working volume.

## Consequences

- The dump protects against a failed migration, not against losing the volume; regular backups stay with the
  operator (master plan 12.5).
- With an external database the operator must back up before updates; `migrate` says so in its log.
- `doctor` doubles as the image smoke test and runs in CI on every pull request.
