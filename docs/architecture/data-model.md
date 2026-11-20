# Data model

The data model in brief: one PostgreSQL database, one schema per module, the main tables of each and how they relate.
Columns are named only where they carry a rule; the migrations in each module's `Persistence/Migrations` are the full
definition. Names are `snake_case`, timestamps `timestamptz` in UTC, primary keys UUID version 7.

## Three kinds of data

Every table belongs to exactly one kind, and the kind decides who may read it and which row-level security policy
guards it ([Security model](../security/security-model.md#tenant-isolation)). A test in the build fails when a table
has no kind or its policy is missing.

| Kind | Tables | What it means |
|---|---|---|
| Global | `intel.*`, `catalog.*`, and in `platform` the jobs, schedules, instance settings, Data Protection keys, worker heartbeats and instance warnings | The same for every organisation. Raw records and entries exist once, however many organisations use them. Jobs carry only identifiers. |
| Organisation | `iam.organization`, `iam.membership`, `iam.invitation`, `assets.*`, `assess.*`, `notify.*` except the three user tables below, `platform.audit_event` | Every row carries `organization_id`, child tables included. Audit events of the instance have none. |
| User | `iam.user` and the other Identity tables, `iam.session`, `notify.notification`, `notify.push_subscription`, `notify.user_preference` | Belong to one account. |

Modules do not navigate into each other's tables. The one foreign key across schemas is `assess.alert.entry_id` to
`intel.security_entry`; elsewhere a module keeps the other module's identifier and reads through its contracts.

## Entries: observations and projection (`intel`)

A consolidated entry is not edited, it is **computed**. Every source delivers raw records; each raw record becomes
one *observation*, the normalised statements of that one source. The entry is the merge of all observations under
fixed precedence rules, with the provenance of every field. Fixes to a parser or to the precedence rules therefore
apply to the past as well: `ardea intel renormalize` and `ardea intel recompute` rebuild observations and entries
from what is stored.

An entry is a `vulnerability` (has an identifier such as a CVE id and is merged across sources) or a `warning`
(has none, such as a phishing warning of the NCSC, and stays bound to its source). An advisory that names twelve
CVEs creates no entry of its own: its observation holds one block of statements per identifier and supports twelve
entries.

| Table | Holds |
|---|---|
| `source` | Each source: kind (`record`, `advisory`, `signal`), operator and operator kind, terms mode (`full`, `metadata_only`), interval, and its fetch state: next due time, cursor, health, consecutive failures |
| `source_run` | One row per fetch: outcome, counts, checkpoints, error |
| `source_record` | The current raw record per source and external id, with content hash, revision and parse status (`quarantined` when it cannot be read) |
| `source_record_revision` | Earlier raw states; the first and the last ten are kept |
| `observation` | The normalised statements of the current raw record, one per raw record, overwritten by each revision |
| `security_entry` | The consolidated entry: primary key (`CVE-…`), state (`published`, `rejected`, `disputed`, `merged` with `merged_into_id`), CVSS, exploitation (`none`, `poc`, `active`) with its evidence, EPSS class, authority warning, fix availability, per-field provenance, a search vector, and the fingerprints that decide whether a change is material |
| `entry_alias` | Every identifier of an entry (CVE, GHSA, OSV, …), unique across entries; a merged entry's keys keep resolving |
| `entry_affected` | Affected products and version ranges: vendor and product, product key, CPE pair, package coordinate, version scheme, range bounds, status and the record's default status |
| `entry_signal` | KEV listing and EPSS score per identifier, written directly by the signal sources without revisions; an entry is recomputed only when its signal class changes |
| `entry_source`, `entry_reference` | Which sources and records support an entry, and its links |
| `entry_change` | The change history: revision, kinds of change, whether material, diff |
| `review_item` | Cases a person decides, such as two entries with different CVE ids joined by a shared identifier |

## Catalogue (`catalog`)

| Table | Holds |
|---|---|
| `product` | The technology catalogue: key, name, vendor, kind, aliases, CPE pairs, package coordinates, version scheme, platform (for plugins), related products, container image names, the `noisy` mark. `origin` is `curated` (from `data/catalog/`, loaded by `migrate`) or `learned`, with state, confidence, reasoning and evidence ([ADR 040](../adr/0040-catalogue-that-learns.md)). |
| `unknown_product` | Product statements from the sources that no catalogue entry covers, with counts and the outcome of learning |
| `learning_event` | What the learning did and why, for review and withdrawal |

## Profile (`assets`)

| Table | Holds |
|---|---|
| `project` | A project or system of an organisation, with criticality (`low`, `normal`, `high`) and exposure (`internet`, `internal`, `isolated`). `kind` is `project` or `organization_stack`: every organisation has exactly one stack, a system project ([ADR 046](../adr/0046-stack-notices-and-reminders-without-deadline.md)). |
| `component` | A technology a project uses: catalogue product key or free vendor and name, kind, version, version scheme and `version_precision` (`none`, `line`, `exact`), packaging (`upstream`, `distribution`), optional exposure, criticality and protection (`none`, `effective`) where the component differs from its project, the notice threshold, a parent component, and for components read from a repository the link, package coordinate, dependency scope and relation, and evidence (file and line) |
| `component_key` | The search index of the candidate search: one row per key of a component (`cpe`, `product`, `purl`) with its strength (`strong`, `weak`) |
| `repository_connection` | An organisation's access to GitHub or a GitHub Enterprise Server: the token encrypted, its hint, kind and expiry |
| `repository_link` | A repository that feeds a project: owner, name, branch, path, state (`ok`, `partial`, `failing`), last commit, the last SBOM (compressed), and a much smaller state held back for confirmation |
| `component_suggestion` | A platform found in a repository (Dockerfile, Compose, project files, …) waiting for a person to accept or dismiss it |

A component keeps its id when its version, context or protection changes. Replacing a project's profile reconciles
by id (or by product key and parent) and removes only what is missing; reading a repository touches only the
components of its own link. A removed component closes its reminder.

## Assessment (`assess`)

| Table | Holds |
|---|---|
| `alert` | One entry from the point of view of one organisation (unique per organisation and entry): its level (the highest of its findings), its current assessment, and a number once a finding reaches level 2 |
| `finding` | One component hit by one entry (unique per alert and component): match type, version verdict (`in_range`, `out_of_range`, `unknown`), verdict and its source (`rules`, `model`, `human`), level, the stored explanation (`factors`), status (`open`, `remediated`, `risk_accepted`, `not_affected`) with reason and note, and the reminder it belongs to |
| `remediation` | The **reminder**: all open findings of one component in one project, with level, target version, exploitation, status (`open` or `closed`, shown as done) and close reason. At most one open reminder per component. |
| `notice` | A «might affect you» notice for an entry that hits a coarse component (unique per organisation and entry): the components that raised it, state (`open`, `affects_us`, `not_affected`, `withdrawn`), who answered, the reminder an «affects us» created, and when and why it reappeared |
| `assessment` | An immutable assessment of an alert: engine (`rules` or `rules+model`), rule set and prompt version, provider and model, input fingerprint, output, tokens and duration |
| `assessment_feedback` | A person's confirmation or correction of an assessment ([ADR 039](../adr/0039-review-of-an-assessment.md)) |
| `assess_event` | The history of reminders, findings and notices; the row's id is the event id the notifications use |
| `org_counter` | The organisation's number counter, shared by reminders and alerts, so a number appears once |
| `match_scope` | Which projects and repository links have been searched, so a first fill is recognised as such ([ADR 035](../adr/0035-actions-history-and-first-fill.md)) |

Findings at level 1 are stored too, as `not_affected` without a reminder or a number: they answer why there was no
alert. Assessment results and their history are never deleted automatically. In the API and in event names a
reminder is still called an *action* ([ADR 046](../adr/0046-stack-notices-and-reminders-without-deadline.md)). How
findings, levels, reminders and notices come about is in [Rule set and levels](rule-set.md).

## Notifications (`notify`)

| Table | Holds |
|---|---|
| `rule`, `rule_target` | A rule (projects, minimum level, events) and its targets: recipient (role, user, external address, webhook), channel, mode (`immediate` or digest with a cron expression in the organisation's time zone) |
| `endpoint`, `external_address` | Webhook targets (URL and secret encrypted) and confirmed external e-mail addresses |
| `pending_event` | The inbox: events handed over by Assessment, evaluated per organisation in order |
| `recipient_state` | What a recipient has been told on a channel about a reminder: last level, last exploitation, fix notified |
| `delivery`, `delivery_event`, `digest_item` | One row per message (also the delivery log); which events an immediate message covers, unique per recipient, channel and event; what waits for a digest |
| `reminder_schedule`, `reminder_state` | The organisation's reminder rhythm per level and the next run; when each open reminder was last reminded |
| `notification`, `push_subscription`, `user_preference` | In-app notifications, Web Push subscriptions and personal settings (muted channels, language): user data |

## Accounts and organisations (`iam`)

The ASP.NET Core Identity tables (`user`, `user_claim`, `user_login`, `user_token`), `session` (server-side sessions:
device, last seen, revoked), `organization` (name, slug, language, time zone, and `settings`: the MFA requirement, the
AI data policy, how much e-mails contain, whether level 5 may be muted, the state of the setup wizard), `membership`
(role per organisation) and `invitation` (hashed token, expiry, role). Other modules read members and organisation
settings through Iam's contracts. Secrets never go into `settings`; they live encrypted in columns of their own.

## Platform (`platform`)

`job` and `schedule` ([Architecture](../architecture.md#jobs-and-schedules)), `setting` (instance settings, secret
values encrypted), `data_protection_key` (the key ring, protected by the master key), `audit_event` (sign-ins and
account changes, memberships and roles, changes to profile, rules and settings, status changes; each with actor, time,
organisation and origin),
`worker_heartbeat` and `instance_warning` ([ADR 043](../adr/0043-instance-warnings-to-administrators.md)).

## Migrations

Each module has its own EF Core migrations with its own history table in its schema; `migrate` applies them in the
order `platform`, `iam`, `catalog`, `assets`, `intel`, `assess`, `notify`, then loads sources, catalogue and rule set.
Migrations go forward only and change the schema compatibly in two steps: first extend, and remove what is no longer
used one version later, so the previous build keeps working against the new schema during an upgrade.
