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

## Language model

Ardea works completely without a language model. With one, it adds an assessment to alerts whose findings reach
*High* or *Critical*, or *Medium* with an unknown version: under which conditions the vulnerability can be
exploited, whether they probably hold here, and what to do first. The model never sets the priority; its verdict
can lower it by one level at most, only where the installed version is unknown, and «probably not affected» is a
proposal a person confirms. At most five assessments per action in seven days are started automatically; people
can ask for more on an alert. Configure providers and limits with the `ARDEA_AI__…` variables
([Configuration](configuration.md)); each organisation chooses in its settings whether nothing, only local
providers, or also cloud providers may be used. The default is «local only».

**Recommended local setup.** [Ollama](https://ollama.com) on the same host or one in the same network, as
provider kind `ollama` (it enforces the answer's JSON schema through a grammar), with an instruction model of
12 to 14 billion parameters under an open licence, for example `qwen3:14b` (Apache-2.0) or `gemma3:12b`. Such a
model writes German, French, Italian and English, which the organisations' languages need, and fits a GPU with
12 to 16 GB of memory; the planning expects 10 to 20 seconds per assessment there. On a CPU it works too, but slowly: raise
`ARDEA_AI__CALL_TIMEOUT_SECONDS` to 300 and keep `ARDEA_AI__PARALLELISM` at 1. Smaller models (7 to 8 billion)
break the length limits and references more often; such answers are refused and asked again once, then the alert
carries the note «without AI assessment». The model has not been measured against Ardea's reference set yet; the
share of corrected assessments per model and prompt version will show its quality in operation.

```
ARDEA_AI__PROVIDERS__local__KIND=ollama
ARDEA_AI__PROVIDERS__local__ENDPOINT=http://ollama:11434
ARDEA_AI__PROVIDERS__local__MODEL=qwen3:14b
```

The worker logs each assessment with provider, tokens and duration, never the prompt or the answer. With
`OTEL_EXPORTER_OTLP_ENDPOINT` set, the meter `Ardea.Assessment.Ai` reports calls, tokens, durations, errors,
retries and assessments by outcome.

## Maintenance commands

Run them in a one-off container: `docker compose run --rm api <command>`. The `org` commands need the owner
role: `docker compose run --rm migrate org …`.

| Command | Purpose |
|---|---|
| `ardea doctor` | Check configuration, database, schema, SMTP and the reachability of the sources |
| `ardea admin reset-password <email> [--disable-mfa]` | Restore access without e-mail |
| `ardea intel renormalize --source <key>` | Read a source's stored records again with the current parser |
| `ardea intel recompute` | Merge every entry again with the current rules |
| `ardea intel import --full [--source <key>]` | Load a source's whole inventory instead of the look-back window; for the CVE Program about 10 GB more |
| `ardea org export <slug> [--out file]` | An organisation's data as JSON, without secrets |
| `ardea org delete <slug> --confirm <slug>` | Remove an organisation and all its data |

## Size

On the reference machine (2 vCPU, 4 GB) the 180-day first load took 51 minutes and 1.9 GB; at rest the
database used about 700 MB of memory and the worker about 230 MB. Raw data grows by about 2 MB a day,
revisions by about 10 MB.
