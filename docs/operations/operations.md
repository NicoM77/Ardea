# Operation

## Services

| Service | Role | Health |
|---|---|---|
| `db` | PostgreSQL 18; data in the `db` volume | `pg_isready` |
| `migrate` | Runs once per start: roles, a dump, migrations, master data. API and worker start only after it succeeded. | exit code 0; 70 with a clear log line on failure |
| `api` | HTTP API and the web app | `ardea healthcheck api` |
| `worker` | Fetches sources, reads repositories, runs all jobs and schedules | `ardea healthcheck worker` (heartbeat) |

Logs are JSON on standard output: `docker compose logs -f worker`. Secrets never appear in them.

## Backups

- `migrate` writes a dump before applying migrations, into `backup/` of the database volume. It is meant for
  going back after an update, not as a backup strategy.
- Back up regularly with `docker compose exec -T db pg_dump -U ardea_migrator -Fc ardea > ardea-$(date +%F).dump`
  and keep `ARDEA_MASTER_KEY` with it, but stored separately.

## Sources

The administration's «Sources» page shows each source's health, last success, next run, records and
quarantine. A failing source backs off up to six hours and recovers on its own; a source with more than 5 %
of its records in quarantine is marked degraded. Its review list holds cases that need a person, such as
two entries with different CVE ids that a shared identifier would join.

## Maintenance commands

Run them in a one-off container: `docker compose run --rm api <command>`. The `org` commands need the owner
role: `docker compose run --rm migrate org …`.

| Command | Purpose |
|---|---|
| `ardea doctor` | Check configuration, database, schema, SMTP and the reachability of the sources |
| `ardea admin reset-password <email> [--disable-mfa]` | Restore access without e-mail |
| `ardea intel renormalize --source <key>` | Read a source's stored records again with the current parser |
| `ardea intel recompute` | Merge every entry again with the current rules |
| `ardea org export <slug> [--out file]` | An organisation's data as JSON, without secrets |
| `ardea org delete <slug> --confirm <slug>` | Remove an organisation and all its data |

## Size

On the reference machine (2 vCPU, 4 GB) the 180-day first load took 51 minutes and 1.9 GB; at rest the
database used about 700 MB of memory and the worker about 230 MB. Raw data grows by about 2 MB a day,
revisions by about 10 MB.
