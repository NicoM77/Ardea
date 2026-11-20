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

- `migrate` writes a dump before applying migrations, into `backup/` of the database volume (the newest three
  are kept). It is meant for going back after an update, not as a backup strategy. With an external database
  that does not let the owner role run server programs, `migrate` says so and writes none: back up before
  updating then.
- Back up regularly with `docker compose exec -T db pg_dump -U ardea_migrator -Fc ardea > ardea-$(date +%F).dump`
  (a cron entry on the host is enough) and keep `ARDEA_MASTER_KEY` with it, but stored separately. Without the key
  everything restores except the stored secrets: tokens, the SMTP password, webhook secrets and the VAPID key pair.
- Everything durable lives in PostgreSQL; API and worker keep no state.

### Restoring

On a new machine or after losing the volume. The order matters: the dump holds rights for the application role, so
the role must exist first.

1. Start only the database: `docker compose up -d db`. Set `.env` and `.env.app` as before, with the same
   `ARDEA_MASTER_KEY`.
2. Create the application role: `docker compose run --rm migrate migrate --roles-only`.
3. Restore into the empty database: `docker compose exec -T db pg_restore -U ardea_migrator -d ardea --no-owner < ardea.dump`.
   If the database is not empty, drop and create it first (see [Update](update.md#going-back)): restoring over
   existing tables fails.
4. `docker compose up -d`. `migrate` brings the schema to the installed version if the dump is older, writes a dump
   first, and gives the application role its rights.

[Update](update.md) shows the same path for going back after an update. A test (`PreReleaseRestoreTests`) restores a
dump of an earlier state, migrates it to the current version, restores again and migrates again.

## Sources

The administration's «Sources» page shows each source's health, last success, next run, records and
quarantine. A failing source backs off up to six hours and recovers on its own; a source with more than 5 %
of its records in quarantine is marked degraded. Its review list holds cases that need a person, such as
two entries with different CVE ids that a shared identifier would join.

## Stack and notices

Every organisation has a stack: a coarse list of products, with no version or a version line, and a threshold per
entry («Only important» or «Everything»). An entry that hits a stack product raises a **notice** («might affect
you»), which people answer on the *Notices* page; «Affects us» creates a reminder. The rules are in
[ADR 044](../adr/0044-notices-per-week-and-thresholds.md) and [ADR 047](../adr/0047-notice-generation-and-answers.md).
What an operator should know:

- **Adding or changing a stack entry** (or its threshold or line) makes Ardea search the last 180 days again for it.
  What it finds is raised quietly: the notices are listed under *Earlier*, apart from the new ones, with one summary
  line in the history. A stack of ten entries can bring a few dozen of them at once. Only entries that arrive
  afterwards are new.
- **Noisy products.** For products the catalogue marks as noisy (Google Chrome, Microsoft Edge, Mozilla Firefox) even
  the threshold «Everything» raises a notice only for active exploitation or an authority's warning. Marking more is a
  curated catalogue change.
- **Recomputing.** `ardea intel recompute` and `ardea assess recompute` ([Maintenance commands](#maintenance-commands))
  also update the notices; there is no separate command.
- **Reminders** have no assignee and no deadline: open or done. Nothing beyond the stack needs configuring.

## Language model

Ardea works completely without a language model. With one, it adds an assessment to alerts whose findings reach
*High* or *Critical*, or *Medium* with an unknown version: under which conditions the vulnerability can be
exploited, whether they probably hold here, and what to do first. The model never sets the priority; its verdict
can lower it by one level at most, only where the installed version is unknown, and «probably not affected» is a
proposal a person confirms. At most five calls per reminder in seven days are made automatically; people can ask
for more on an alert, up to a daily cap per organisation. The model sees the vulnerability's public texts and, of
the organisation, only the affected components' version, kind, exposure, criticality and whether a protection is
in place, under placeholders: no names of components, projects or the organisation, and no free text such as the
protection note. Configure providers and limits with the `ARDEA_AI__…` variables
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

## Catalogue that learns

Records name many products the curated catalogue does not know. Once a day (02:41 UTC), an hour after a source's first load, and when the instance
administrator starts it on *Sources → Catalogue* or with `ardea catalog grow`, the catalogue collects these unknown
product keys with the number of records naming them, maps what rules can map (a known CPE pair or package, the same
name but for version, legal form or a leading vendor name; an unknown CPE pair or package becomes a new entry) and,
with a provider, asks the model about the most frequent rest: keys from two records on, at once when one is in KEV,
at most `ARDEA_AI__CATALOG_DAILY_CALLS` calls a day. Answers with high confidence that pass the checks take effect;
everything else waits on the review list on *Sources → Catalogue*, where it is confirmed, changed or rejected. A
product name or record title that reads like an instruction never takes effect without a person. Learned entries only add: what the
curated catalogue finds stays as it is, and a hit that came about through a learned entry says so on the finding and
can be taken back with one click. The same page exports the learned entries as catalogue files for a contribution to
the project and shows the measures. Without a provider only the rules map; the rest stays on the list of unknown
products for the administrator. Which provider is used is chosen there or with `ARDEA_AI__CATALOG_PROVIDER`; by
default the first local one.

**Observing it with a local model (phase 4 acceptance, manual).** Once per prompt version, a developer checks the
model on real data; this is not part of the pipeline and has not been run yet for prompt `learn-1`.

1. Start Ardea from a checkout with the first load done (`ARDEA_INTEL__BACKFILL_DAYS=180`) and a local provider, e.g.
   Ollama with `qwen3:14b` as above. Set `ARDEA_AI__CATALOG_DAILY_CALLS=100`, so that 50 keys get their two attempts.
2. Run `ardea catalog grow` once and wait until the worker logs «Catalogue grew» with `continues: False`, or repeat
   it until 50 keys were asked: `SELECT count(*) FROM catalog.unknown_product WHERE attempts > 0`.
3. Take the 50 most frequent keys that were asked:
   `SELECT raw_key, vendor, product, outcome, product_key, proposal FROM catalog.unknown_product WHERE attempts > 0 ORDER BY record_count DESC LIMIT 50`.
4. Judge each against its records by hand: right alias, right new product, or wrong. Count among those that took
   effect (`alias`, `new` with `learned_by` `model`) the wrong ones, and the share that took effect at once.
5. The acceptance holds when no wrong mapping took effect at once and at least half took effect at once. Record the
   model, the prompt version, the date, the counts and every wrong mapping in `docs/acceptance/phase-4.md`; withdraw
   the wrong ones on *Sources → Catalogue*.

The worker logs each call with provider, calls, tokens, outcome and duration, never a product name or an answer;
the meter `Ardea.Catalog.Learning` counts calls, retries and keys by outcome.

## Notifications

Organisations decide in their settings who is told what, on which channel and how fast (rules with targets, digests,
personal muting); the instance provides the channels:

| Channel | What the instance needs |
|---|---|
| In the app | nothing; the inbox and the unread counter |
| E-mail | SMTP ([Configuration](configuration.md); the administration sends a test e-mail). Without SMTP the channel is off and the interface says so. Every message to an external address carries an unsubscribe link, so confirm and unsubscribe links need `ARDEA_PUBLIC_URL` to be right. |
| Webhook | Outbound HTTPS to the target. Targets in a private network are refused unless their host is in `ARDEA_WEBHOOK_INTERNAL_HOSTS`; loopback, link-local and cloud metadata addresses never work. Requests are signed (`Ardea-Signature`, HMAC-SHA256 with the endpoint's secret) and may arrive twice: receivers drop a repeat by the delivery id. |
| Web Push | HTTPS (browsers allow it nowhere else) and outbound HTTPS to the browser makers' push services: `fcm.googleapis.com`, `updates.push.services.mozilla.com`, `web.push.apple.com`, `*.push.apple.com` and `*.notify.windows.com`. Other push services only through `ARDEA_PUSH_SERVICE_HOSTS`. Each person turns it on per device in their settings; on iPhone the web app first has to be added to the Home Screen. |

**VAPID key pair.** Web Push needs the instance's own key pair. Without `ARDEA_VAPID__PUBLIC_KEY` and
`ARDEA_VAPID__PRIVATE_KEY` the first start generates one and keeps it encrypted in the database, so it lives and dies
with the database backup and the master key. A new pair makes every device subscribe again; devices whose
subscription the push service rejects are removed after the third rejection in a row.

**Limits.** Immediate messages to one recipient on one channel wait two minutes and are bundled. Above 60 immediate
messages per channel, organisation and hour, further ones are suppressed (they stay in the digests that cover them).
A failed delivery is retried with growing pauses for about three and a half hours; then it is `dead`. If a level 5
message dies and its recipient was reached nowhere else, the owners get a warning in the app. The delivery log of an
organisation (*Notification rules → Log*) shows state, attempts and the reason for every message. Confirmation mails to
external addresses are limited against abuse: 50 addresses per organisation, 3 mails per address with 15 minutes
between them, 20 per organisation a day.

## Repositories

An organisation connects GitHub with a fine-grained read-only token (*Contents: read*, selected repositories, with an
expiry) that its owners or admins store once; it is kept encrypted and shown only by its last four characters and
expiry. Public repositories work without a token but GitHub allows only 60 requests an hour per address of the
instance, which suits trying it out, not many repositories. GitHub Enterprise Server hosts are added by the instance
administrator in the administration. The worker reads each linked repository about every hour; a conditional request
that answers «unchanged» does not count against the token's quota. A link whose token expired, whose repository
vanished or whose dependency graph is switched off shows its reason and since when.

## Warnings and figures

The system warns about itself. Every signed-in person sees the warnings on the overview («data as of 02:10, source
not reachable»); the instance administrator also sees the operating figures in the administration, card *Operation*.
Figures are counts and times, never contents.

| Warning | When |
|---|---|
| `worker_stalled` | no worker has beaten for two minutes, or none has ever started: nothing is fetched or assessed |
| `source_stale` | an enabled source has had no success for six hours (or none six hours after it was added) |
| `notify_events_failed` | events that could not be evaluated after five tries were set aside |
| `deliveries_failing` | messages of the last 24 hours that every attempt failed to deliver |

Figures: per source the lag, the failure streak and the quarantine; jobs waiting (with the oldest wait), running and
failed of the last day by kind; workers alive; deliveries by state; repository links by state; per language model
provider the calls, tokens and twice-refused answers of the last day. The figures can also be scraped with
OpenTelemetry (`OTEL_EXPORTER_OTLP_ENDPOINT`).

**Mail to the instance administrators** ([ADR 043](../adr/0043-instance-warnings-to-administrators.md)). Every five
minutes the instance checks itself, in the `api` and in the `worker` container (one of them at a time; the API
also reports a worker that has stopped). Every instance administrator with a confirmed e-mail address gets one
short mail per round with what is new, a reminder once a day while a warning lasts, and one mail when it is over
(after it stayed clear for a round, so a warning that comes and goes does not flood the inbox). The mail holds the
warning's message and a link to the administration, nothing else. The card *Operation* shows when the
administrators were last mailed about a warning. Without SMTP nothing is mailed and nothing fails: the warnings
stay on the overview and in the administration, and each container says so once in its log (event 1502; event
1503 when no instance administrator has a confirmed address). A mail that reached no administrator is tried again
by the next round; one that failed only for some (event 1505 names the account) is not repeated for the others,
and those it failed for hear of a lasting warning with the daily reminder. Across hosts, keep the clocks of the
`api` and `worker` hosts synchronised: the worker's heartbeat is compared with the clock of the process that checks.

**Image contents.** The image carries `/app/LICENSE` (Ardea's licence) and `/app/THIRD-PARTY-NOTICES.md` (the
licence of every NuGet and npm package it ships). The bill of materials is produced by `build/sbom.sh` (CycloneDX
JSON, one file for the .NET host and one for the web app); CI keeps both as a build artefact on `main`.

## Maintenance commands

Run them in a one-off container: `docker compose run --rm api <command>`. The `org` commands need the owner
role: `docker compose run --rm migrate org …`. `seed-demo` needs the owner password and the settings of
`.env.app`: see [Try it](try-it.md).

| Command | Purpose |
|---|---|
| `ardea doctor` | Check configuration, database, schema and the image's runtime; it also tries to connect to the SMTP server, every language model provider and the hosts of each source named in the environment. A service that cannot be reached is a `warn` line (it may be down only for now); a wrong provider setting fails. Settings made in the administration instead of the environment are not seen. |
| `ardea version` | The installed version |
| `ardea admin reset-password <email> [--disable-mfa]` | Restore access without e-mail |
| `ardea intel renormalize --source <key>` | Read a source's stored records again with the current parser |
| `ardea intel recompute` | Merge every entry again with the current rules |
| `ardea assess recompute` | Assess every alert again with the current rule set, e.g. after a new rule set version; this also updates notices and reminders |
| `ardea intel import --full [--source <key>]` | Load a source's whole inventory instead of the look-back window; for the CVE Program about 10 GB more |
| `ardea catalog grow` | Let the catalogue learn from the unknown products of the sources now, e.g. after the first load |
| `ardea org export <slug> [--out file]` | An organisation's data as JSON, without secrets |
| `ardea org delete <slug> --confirm <slug>` | Remove an organisation and all its data |
| `ardea seed-demo` | Load the sample data (with a stack and notices) into an empty instance ([Try it](try-it.md)); needs the owner role, refuses a database with data |

## Size

On the reference machine (2 vCPU, 4 GB) the 180-day first load took 51 minutes and 1.9 GB; at rest the
database used about 700 MB of memory and the worker about 230 MB. Raw data grows by about 2 MB a day,
revisions by about 10 MB.
