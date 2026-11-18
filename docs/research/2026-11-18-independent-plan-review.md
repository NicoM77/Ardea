# Independent review: CSH Alert master plan

> This review was written against the first complete draft of the plan. The plan has since been revised; chapter 15.4 of the master plan lists what was changed in response and what was left as is. Section and chapter numbers below refer to the draft that was reviewed.

Reviewed 18 Nov 2026. Review only; no file in the repository was changed.

**Read completely:** `docs/masterplan/README.md` and chapters 01 to 15, `docs/adr/README.md`, `prototypes/relevance-funnel/README.md` and `funnel.py`, `PRODUCT.md`.
**Skimmed or spot-checked:** `DESIGN.md` (tokens, contrasts, breakpoints), the text of the three mockups plus one phone screenshot, the three research files (targeted reads of the sections the plan cites).
**Own checks:** recomputed the worked examples in 7.5, recomputed the contrast ratios in 11.3, and applied rule set v1 to the spike's 173 candidates (the reviewer's throwaway script; its successor is `prototypes/relevance-funnel/levels.py`).

References such as "decision 15.2/E10" mean the owner decisions in chapter 15.2 of the plan; plain E1 to E5 are the feasibility findings of this review.

Severity: **high** = should be resolved before the plan is approved; **medium** = resolve before the affected phase starts, an implementer would otherwise guess or build the wrong thing; **low** = correct when convenient.

Counts: 6 high, 28 medium, 18 low.

## The six things I would not approve as written

1. **C1** The headline promise "173 Kandidaten und eine Handvoll Meldungen" is not measured. Applying the plan's own rule set to the spike's own candidates gives between 6 and 173 alerts at level 3 or higher per week, depending only on two profile attributes; 167 of the mockup's "level 1" records are level 2 under the rules.
2. **B1** The notification dedupe key contradicts the guarantee it is supposed to implement, and the default rules collide under it.
3. **D2** The language model can set an alert to level 1 whenever the version check says "unknown". The floors that are supposed to stop a manipulated source text do not apply there.
4. **D1** Row-level security is described with "one exception". There are at least ten cross-tenant paths, several user-scoped API routes with no tenant, and no design for one transaction across per-module DbContexts.
5. **A1** The launch source NCSC has no defined effect on anything a user sees, and its task and acceptance criterion hang on an external answer with no fallback.
6. **E1** 16 to 23 weeks for one person is not credible for this scope, and the MVP is not lean.

---

## A. Coverage gaps against the brief

### A1 · high · NCSC/BACS: a launch source without a defined product effect, gated on an external answer
**Where:** 05 §5.5, 06 §6.5, 07 §7.5, 08 §8.2, 11 §11.4, 14 Phase 1 (task 1.9 and acceptance), 15 §15.2 (E10).
**Problem:** The brief names NCSC as one of two initial sources. The plan ingests it but never says what a user gets from it.
- Warnings with a CVE "werden Beleg des jeweiligen Eintrags und heben dessen Aufmerksamkeit" (5.5). No factor in 7.5 and no event in 8.2 implements "Aufmerksamkeit".
- Warnings without a CVE (9 of 12 in the sample, 7 of them phishing) become entries of kind `bulletin`. "Ohne KI-Anbieter bleibt eine solche Warnung ein Bulletin ohne Projektbezug" (6.5). `none` is the default provider (decision 15.2/E6). No page in 11.4 shows such entries (the "Einträge" page of task 1.10 is not part of the application structure), and no event in 8.2 notifies about them.
- Task 1.9 runs "nach Klärung mit dem BACS", and the Phase 1 acceptance criterion requires "einer NCSC-Warnung" as one of four pieces of evidence. If BACS answers late or says no, Phase 1 cannot be accepted. The interim mode (store title, short text, link) is also reproduction in the strict reading the plan itself gives in 15.1.
**Fix:** Add a section "What NCSC content does in the product": (a) a rule-set factor or a tag "national CERT warning" with a defined effect; (b) a place in the bulletin page for source warnings without project relation, and an optional rule event `source.warning_published`; (c) a timebox for decision 15.2/E10 and a decided fallback (link-only mode with title, or NCSC behind a feature flag and Phase 1 accepted with three sources). Rewrite the Phase 1 criterion so it does not depend on BACS.

### A2 · medium · "Existing protections" is missing as a prioritisation input
**Where:** 07 §7.5 (factor table), 04 §4.5.
**Problem:** The brief lists five inputs beyond CVSS: active exploitation, exploit availability, exposure, criticality, existing protections. The rule set has no factor and the profile no field for protections (WAF, segmentation, compensating controls). The word "Schutzmassnahmen" appears only for prompt-injection safeguards (7.4).
**Fix:** Either add a small optional component attribute (for example `mitigations`: none, partial, strong, with a note) and a factor of 0 / −0.5 / −1 that never overrides the floors, or state explicitly in 1.5 that this input is deferred and why.

### A3 · medium · The default installation (no AI) is the one experience that is neither specified nor mocked
**Where:** 07 §7.1 point 1 ("Begründung aus Vorlagen"), 11 §11.4 (state "ohne KI-Einschätzung"), 01 §1.6 ("Ohne KI"), 15 §15.2 (E6).
**Problem:** With `none` as default, most instances will show template text. The plan does not say where "what happened" and "what to do" come from without a model. CVE descriptions are technical prose, and structured remediation is sparse (patch-tagged reference in 32.2 % of records per the spike). All mockups show the AI variant. "Ohne KI vollständig" is an assertion.
**Fix:** Specify the rule-only templates (for example "Upgrade {component} to {fixed} or later" derived from the version range, else the vendor advisory link, else "No fix listed"), mock the no-AI alert at 390 px, and add it to the Phase 3 acceptance.

### A4 · medium · Operating systems and distribution packages cannot be matched with the MVP sources; backports are not mentioned
**Where:** 11 §11.6 step 3 (group "Betriebssystem"), 06 §6.6, 07 §7.2 and §7.3, 01 §1.5 (distributions only in the first wave after 1.0).
**Problem:** The first profile category of the brief is the operating system, and example project C contains "Linux". With CVE Program data only, (a) the Linux kernel yields 35 candidates per week in the spike, most with git-commit ranges; (b) software installed from distribution packages carries backported fixes, so comparing the package version with the upstream range gives systematic false "in range" results. The words backport and distribution package do not occur in the plan.
**Fix:** Add a component attribute "installed from distribution X" (or `origin = distro`). Until Debian, Ubuntu and Red Hat data arrive, such components get the version verdict `unknown` with an explicit reason instead of `in_range`. Decide how the kernel is handled in the MVP (for example only KEV-listed or with distribution data) and say so in 1.5.

### A5 · medium · Wizard categories: two brief items missing, two have no matching semantics
**Where:** 11 §11.6, 04 §4.5.
**Problem:** The brief asks for criticality "of systems and business processes"; only a three-step project and component criticality exists. Virtualisation is not a group in step 3. "SaaS" and "Cloud und Hosting" are groups, but nothing says how a service without installable versions is matched or what verdict it gets (the worked example "SQL Server vs Azure SQL" shows the problem and is not derivable from the rules, see B2). Dependencies are deferred to SBOM import, which is disclosed.
**Fix:** State for each brief category either the field that captures it or that it is deferred. For hosted services define a component kind `service` whose matches are only raised when the record marks the service itself as affected, never by product-family similarity.

### A6 · low · Who may create an organization, and e-mail verification
**Where:** 09 §9.1, 10 §10.2 (`POST /orgs`, `POST /auth/register`), 04 §4.8.
**Problem:** With the default "nur auf Einladung", invitations are organization-scoped. How a second organization comes into existence (any user via `POST /orgs`? an instance-level invitation?) is not said. E-mail verification on registration is not mentioned, although rules can send to addresses.
**Fix:** One paragraph in 9.1 and an instance-level invitation or setting "who may create organizations".

### A7 · low · "Likelihood" has no explicit output
**Where:** 07 §7.4 (output schema).
**Problem:** The brief asks eight questions; seven map to fields, likelihood is only implicit in EPSS and preconditions.
**Fix:** Add a table brief question → field, and either a `likelihood` field or a note that EPSS plus preconditions is the answer.

---

## B. Internal contradictions

### B1 · high · Notification dedupe: the key contradicts the guarantee, and the default rules collide
**Where:** 08 §8.4, §8.3, §8.8; 04 §4.7; ADR 014; README ("Alarmierung"); 01 §1.6.
**Problem:**
- 8.4 defines `dedupe_key = hash(Regel, Empfänger, Kanal, Fingerabdruck)` and claims in the next sentence that "zwei Regeln mit demselben Empfänger und Kanal erzeugen damit höchstens eine Zustellung". With the rule in the hash, two rules give two keys and two deliveries.
- README and 1.6 promise at most one message "je Empfänger, Kanal und Sachverhalt"; ADR 014 says "je Regel, Empfänger, Kanal und Fingerabdruck". These are different guarantees.
- The three default rules overlap by design: a level 5 alert matches rule 1 (in-app, e-mail, push at once), rule 2 (in-app at once, e-mail digest) and rule 3 (weekly e-mail). Under the written key an owner gets two in-app notifications immediately. Criterion 8.8 ("pro Empfänger und Kanal genau eine Zustellung") fails on a fresh organization.
- Default rule 2 is "In-App sofort, E-Mail gesammelt", but `notify.rule` has one `mode` per rule (4.7). The rule cannot be stored as described.
**Fix:** Remove the rule from the key. Rules only route; uniqueness is per (alert, recipient, channel, state transition). Move `mode` to `rule_target` (or make rule 2 two rules and say "four default rules"). Align ADR 014, README and 1.6. See D4 for the state model.

### B2 · medium · Rule set 7.5: arithmetic is right, definitions and thresholds disagree
**Where:** 07 §7.5; 14 Phase 3 acceptance.
**Problem:** All six sums are correct (appendix 1). But:
- Level 4 is defined as "betroffen oder wahrscheinlich betroffen mit ernster Auswirkung". High severity (3) + version in range (1) + normal (0) + internal (−0.5) = 3.5, which is level 3.
- Level 3 includes "ernste Auswirkung bei unsicherer Betroffenheit". High (3) + unknown (0) + normal (0) + internal (−0.5) = 2.5, which is level 2.
- Level 5 is defined as "betroffen oder sehr wahrscheinlich betroffen". Critical (4) + active (3) + version unknown (0) + internet (1) = 8, which is level 5 with unknown affectedness.
- The PostgreSQL and MySQL examples both land on exactly 4.5, the threshold. The examples fit the thresholds; nothing shows the thresholds fit real data (see C1: one attribute moves 100 alerts across a threshold).
- The seventh example ("Azure SQL nicht unter den betroffenen Produkten" gives level 1) matches none of the four level-1 rules listed below the table. Phase 3 requires "die Beispielfälle aus Kapitel 7.5", before the AI exists.
- The floor of 5 for "aktiv ausgenutzt, Version im Bereich, Schweregrad hoch" ignores exposure and criticality completely: an isolated, low-criticality system (sum 4.5) is raised to Critical with a 48-hour deadline and an immediate push.
**Fix:** Either reword the level definitions to what the sums produce or move the thresholds. Add a rule for product-variant mismatch or drop the seventh example from the acceptance. Make the floor of 5 conditional on exposure not being `isolated` (floor 4 otherwise). Calibrate thresholds on the spike data, not on six examples.

### B3 · medium · MVP table (1.5) versus phases (14) versus chapters
**Where:** 01 §1.5; 14 tasks 3.3, 3.9, 4.5, 4.7, 1.10; 04 §4.6; 08 §8.2 and §8.7; 10 §10.2; 11 §11.4 and §11.5.
**Problem:**
- Assignment and deadlines: 1.5 places "Zuweisung mit Fristen" in Ausbaustufe 1. Tasks 3.3 ("Fristen") and 3.9 ("Zuweisung"), the columns `assigned_to` and `due_at`, the API (`PATCH …/alerts/{number}`: "Zuweisen, Frist ändern"), the event `alert.assigned`, 8.7 ("Fristen sind im MVP sichtbar") and the mockups all put them in the MVP.
- Corrections as context: Ausbaustufe 1 in 1.5, task 4.7 before 1.0.
- AI extraction from free text: task 4.5 and a second model role in 7.7; not in the MVP column.
- Task 1.10 builds a page "Einträge". 11.4 and the feature folders in 11.7 have no such area.
- 1.5 says "einem Kanal-Trio"; the same row lists four channels and Phase 5 says "vier Kanäle".
**Fix:** Decide each item once and make 1.5 the single source. If the MVP is to be lean (E1), tasks 4.5 and 4.7 are the obvious ones to move out.

### B4 · medium · Three event vocabularies, and 7.6 contradicts 8.2
**Where:** 03 §3.3; 04 §4.6 (`alert_event.kind`); 07 §7.6; 08 §8.2.
**Problem:** Chapter 3 knows two integration events (`AlertRaised`, `AlertLevelChanged`). 8.2 lists eleven (`alert.newly_affected`, `alert.exploitation_escalated`, `alert.fix_available`, `alert.withdrawn`, `backfill.completed` and others). `alert_event.kind` has seven values and lacks most of them. 7.6 says a reassessment changes the alert "nur, wenn sich Stufe, Urteil, Massnahmen oder betroffene Projekte ändern", but 8.2 needs `exploitation_escalated` "ohne Stufenwechsel" and `fix_available`.
**Fix:** One table in chapter 7 or 8: event, producer, condition, stored as which `alert_event.kind`, notifies yes or no. Add exploitation state and fix availability to the list in 7.6.

### B5 · medium · Level per project is used everywhere but stored nowhere
**Where:** 04 §4.6 (`finding` has no level), 07 §7.5 ("Die Stufe der Meldung ist das Maximum über ihre Projekte"), 10 §10.2 (`/bulletin`: "Stufe je Projekt"), 08 §8.3 (rules by project and minimum level), 11 §11.4.
**Problem:** The rule set computes a level per project, the bulletin shows one and rules filter by project and level, but only `alert.level` and `assessment.level` exist. Consequences: a rule for project B with minimum level 4 fires on an alert whose level 5 comes from project A. After project A is remediated the alert stays at 5 although only a level 2 finding in B is open. A finding has one `component_id`; two matching components in one project are not modelled.
**Fix:** Add `level` and `factors` to `finding` (one finding per alert, project and component, or a child table for components). Define `alert.level` as the maximum over open findings. Evaluate rules on the finding level.

### B6 · medium · Job keys and retry mechanisms contradict each other
**Where:** 03 §3.3 and §3.4; 04 §4.4, §4.5, §4.6, §4.7; 05 §5.4; 08 §8.6.
**Problem:**
- `MatchEntry(entryId, revision)` is idempotent "über Schlüssel (Eintrag, Revision)", and the paragraph below says it is coalesced "über seinen Schlüssel", "sodass eine Welle von Änderungen eine Einschätzung auslöst, nicht fünf". Five revisions are five keys.
- `dedupe_key text unique` over all states blocks a new job with the same key for the 7 days a succeeded job is kept. Coalescing needs a partial unique index on pending jobs, and a new pending job must be allowed while one with the same key is running.
- `AssessAlert` is keyed on "Profilversion" (3.3), `assessment` stores `profile_fingerprint` (4.6), `project` has `profile_version` (4.5). An alert spans several projects, each with its own version; which one is in the key?
- Retries are specified three times: the job table (exponential, `max_attempts`, `dead`), `notify.delivery` (1, 5, 30 minutes, 2 and 6 hours, `dead`, own `attempts`), and the source (`next_sync_at`, `consecutive_failures`, own `lease_until`). `Deliver(deliveryId)` is also a job. Which mechanism governs?
**Fix:** Key `MatchEntry` on the entry only and read the latest material revision at run time. Partial unique index `WHERE state = 'pending'`. Key assessments on a fingerprint of the matched components. One retry and lease mechanism (the job table, with a retry policy per job kind); source and delivery rows keep domain state only.

### B7 · medium · "Reproduzierbar" and "es vergibt keine Stufe" overstate what 7.5 specifies
**Where:** 07 §7.1 point 2, §7.5; README ("Wie es das tut"); ADR 007; 14 Phase 3 acceptance.
**Problem:** 7.1 requires "in jeder Ausbaustufe": "Gleicher Eintrag, gleiches Profil, gleiche Regelversion ergeben dieselbe Stufe." With a model configured and the version unknown, the model's verdict moves the sum by up to 2.5 points (+1 to −1.5) or sets level 1 directly. The level then depends on model, prompt version and a non-deterministic output. The spike suggests "unknown" is common, not an edge case: about 58 % of the counted version entries are of type `custom` (14 569 of 25 270), and 26 % of records have no range.
**Fix:** State the guarantee as: same entry, profile, rule version and stored model verdict give the same level. Say in the README that the model influences the level through one bounded factor. Measure in the spike how many candidates end as `unknown` with the planned comparers.

### B8 · low · Small count and name mismatches
- 03 §3.2: "sechs Module und zwei Hosts" and an `Api` and `Worker` node in the diagram; 13 §13.1 and ADR 002: one host project, five module folders plus Platform.
- 01 §1.3: "drei Container"; the Compose file has four services.
- Wave 1 after 1.0: ten named sources in 1.5 and in 14, eleven in 5.6 (CERT-FR only there).
- AI providers: five in 2.3, three plus `none` in 7.7, two plus `none` in decision 15.2/E6, one configurable in 12.4.
- 7.1: "in Minuten erledigt"; 7.9: "30 bis 60 Minuten".
- `finding.verdict` has four values (4.6), the model output five (7.4, with `uncertain`). The API example shows `versionVerdict: in_range` with `verdict: likely`.
- 4.2 draws observation to entry; the table `entry_source` links `source_record_id`.

### B9 · low · Escalation: "Das Datenmodell … trägt das bereits"
**Where:** 08 §8.7, 04 §4.7. No step, delay or escalation target exists in `rule` or `rule_target`. Add the columns or drop the sentence.

### B10 · low · Rate limits "in der Datenbank"
**Where:** 03 §3.7 ("Obergrenzen je Quelle bleiben instanzübergreifend gültig, weil sie in der Datenbank liegen") versus 02 §2.1 (`System.Threading.RateLimiting`, in-process). The backend research says the limiters are in-process and several workers "need a Postgres-backed lease/token table". Only the one-run-per-source lease is in the database; sources sharing a host (GitHub: CVE deltas, CISA CSAF, GHSA, Metasploit, Nuclei) are not covered by it.

### B11 · low · EPSS classes differ
**Where:** 06 §6.4 (four classes with a boundary at 1 %) versus 07 §7.5 (three classes). Crossing 1 % triggers a reassessment that cannot change a point. Align the classes.

### B12 · low · Tenant data volume
**Where:** 04 §4.9 ("einige tausend Zeilen pro Organisation und Jahr") versus 04 §4.6 and 07 §7.2 (level 1 results and weak matches are stored per organization). The mockup alone shows 167 level 1 records per week; a WordPress profile adds 477 weak matches per week. That is tens of thousands of alert rows per year, each with a consecutive `number`. Either aggregate weak matches into a counter or correct the estimate and say whether level 1 rows take a number.

---

## C. Claims and the evidence files

More than 45 numbers and version or licence statements were checked (appendix 2). Versions, dates and licences in chapter 2 match the research almost everywhere. The problems are concentrated in a few places.

### C1 · high · "Eine Handvoll Meldungen" is not measured, and the plan's own data contradicts it
**Where:** README ("Wie es das tut"); 07 §7.1 (column "Gemessene Grössenordnung pro Woche", row 4: "davon Stufe 2 bis 5: eine Handvoll"); 07 §7.6 ("2 kritisch, 5 hoch"); mockup `bulletin.html` ("173 were checked in depth; 6 concern you", "167 records at level 1").
**Problem:** The spike measures candidates only. Its README says "keine Prüfung der Trefferqualität über Stichproben hinaus". It also says that 110 of the 173 candidates are high, critical or exploited (62 + 7 + 41) and that the version filter helps "nur mässig" (.NET 22 to 14, SQL Server 53 to 48, PostgreSQL 52 to 47). Under 7.5 every strong match that is not out of range is at least level 2; level 1 needs out-of-range, a vendor statement, a weak match, a withdrawal or the model.

I applied rule set v1 to the 173 candidates with the spike's own matching (EPSS taken as 0, criticality normal):

| Assumption for all candidates | L5 | L4 | L3 | L2 | Level 3 or higher |
|---|---:|---:|---:|---:|---:|
| version unknown, internal | 0 | 2 | 4 | 167 | 6 |
| version unknown, internet | 2 | 4 | 164 | 3 | 170 |
| version in range, internal | 2 | 4 | 104 | 63 | 110 |
| version in range, internet | 2 | 108 | 63 | 0 | 173 |

- The mockup's "6" and "167" reappear only in the first row, and there the 167 are level 2 (Low, 90 days, in the default list), not level 1.
- The better the profile (versions known, internet-facing), the more alerts: about 110 at level 4 per week for this organization. That is not a handful and contradicts principle 1.
- Onboarding: 180 days of lookback is roughly 3 000 to 4 500 candidates for the same organization, not "2 kritisch, 5 hoch". With a local model at 10 to 20 seconds that is 8 to 25 hours of model time before the "first bulletin" is complete; 7.9 only costs the steady state.
**Fix:** Before approval, extend the spike: implement the planned version comparers and rule set v1 on the 173 candidates and report the level histogram for realistic profiles, and the share of `unknown`. Then recalibrate. The lever most likely to reach "a handful" is an aggregation unit above the single CVE: one actionable item per component and fixed version ("PostgreSQL 17.9 to 17.12 fixes 47 vulnerabilities, highest level 4"), used in the bulletin, in notifications and in digests. Also limit what the lookback assesses (for example exploited or critical entries, 30 to 90 days) and call the model only for candidates the rules place at level 3 or higher or on opening. Replace "eine Handvoll" in the README with the measured figure, and mark the "6" in the mockup as illustrative.

### C2 · medium · Numbers that the committed evidence does not contain
**Where:** 06 intro ("Die Zahlen stammen aus dem Daten-Spike"), §6.3, §6.4; 04 §4.9; 05 §5.5; README point 1; 15 §15.1.
**Problem:**
- "85 326 Änderungsereignisse in 30 Tagen bei 17 642 neuen Records", "über hundertmal geändert", "im Spike bei 25 % der Records" ohne Titel: none of these is in the spike README or produced by `funnel.py`. I could reproduce all three from `deltaLog.json` and the week data in the author's session scratch directory (17 642 new, 85 326 updated, two records changed 126 times, 1 609 of 6 432 without title), so they are correct, but the repository does not record them and the scratch directory is not part of it.
- The whole table "Weitere Messwerte" and the version-filter figures in the spike README are not computed by `funnel.py` either.
- "tägliches Gesamtarchiv von 666 MB": no evidence file contains this.
- "Die Menge hat sich vervierfacht" and "Vierfaches gegenüber 2025": no evidence file contains a 2025 baseline.
- 7.1 labels the 6 432 records "neu oder wesentlich geändert"; the spike counts every record new or changed in any way.
- The spike README is inconsistent with itself: .NET has 23 structured candidates in the table and "22 → 14" in the text.
- That 180 daily archives are retrievable for the backfill ("über die Tagesarchive des Rückblicks", 5.5) is not verified anywhere.
**Fix:** Commit a second script (or extend `funnel.py`) that prints every figure the plan cites, and store its output next to the README. Add the source of the 2025 baseline and of the archive size, or remove the two statements.

### C3 · medium · Stated as verified although the research says unverified, prior knowledge or aborted
**Where and what:**
- 05 intro: "Alle Endpunkte in diesem Kapitel wurden am 18.11.2026 tatsächlich abgerufen." The catalogue marks CVE Program, KEV and the EPSS API as "given" (not fetched by the research), the Cyber Security Hub list as UNVERIFIED (5.5 does say this), and states that "the research run was stopped before its closing sections were written". The synthesis in chapter 5 (families, waves, sources to avoid) is therefore the plan's own and is not backed by a research summary. The plan does not disclose this.
- 05 §5.6 and README ("geprüfter Quellenkatalog"): only Oracle and Palo Alto are flagged as "Inhalte ohne freie Lizenz". The catalogue marks the terms of at least twelve planned sources as UNVERIFIED (CERT-EU, NCSC-NL, CERT-FR, ACN, JVN, Fortinet, Atlassian, GitLab, Jenkins, Drupal, CERT/CC, EUVD), finds "no open licence" for CERT.at, and notes non-commercial terms (unverified) for the Shadowserver data behind the CIRCL sightings.
- 05 §5.4: NVD "5 Anfragen in 30 Sekunden" as a documented limit; the catalogue says "not re-read today", the backend research "from prior knowledge, re-check".
- 02 §2.3: local models "bei 6 bis 10 GB Speicher"; the research calls the memory figures "rough Q4 estimates, UNVERIFIED". Not listed in 2.8.
- 02 §2.6 and 13 §13.5: "MassTransit ab 9, FluentAssertions ab 8"; the last free versions are UNVERIFIED in the research (item 5 of its list).
- 02 §2.2: Hangfire "Transaktional: nicht vorgesehen"; the research records absence of evidence.
- 11 §11.9: "Android zusätzlich über UnifiedPush ohne Relay"; the research could not reach the specification.
- README (table of decisions) and 01 §1.3 principle 9 present three-layer separation down to the database as a carrying decision. The backend research concludes: "Treat RLS as an optional hardening spike, not a foundation." The plan flags the spike but reverses the recommendation without saying so.
**Fix:** Reword the intro of chapter 5, add a column "Bedingungen geprüft: ja/nein" to 5.6, add the missing rows to 2.8, and state in decision 15.2/E5 that the plan deliberately goes beyond the research recommendation.

### C4 · low · Smaller mismatches
- 02 §2.1: orval is the "einziger aktiv gepflegter Generator mit Ausgabe für `httpResource`". The research lists OpenAPI Generator as maintained with `withHttpResource=true`; orval is the only one whose default output matches the split.
- 02 §2.2: "Rund 300 Zeilen"; the research says "~200 lines" for a plain outbox and worker loop, which is a much smaller scope than 3.4 (see D6).
- 11 §11.9: "Die App-Stufe ändert am Server nichts ausser der zweiten Anmeldeart." The research requires default-on CORS for the two Capacitor origins and an unauthenticated meta endpoint; 12.2 says CORS "entfällt".
- 02 §2.1: the plan stays on .NET 10 for maturity but lists the 11.0.0 line (eight days old) for Microsoft.Extensions.AI and Http.Resilience; for the latter the research did not look up the 10.x line. Pin the 10.x packages or confirm in Phase 0.
- 02 §2.4: "Node 22 oder 24"; the research also lists 26.

### C5 · low · Mockup mixes measured and invented figures
**Where:** `bulletin.html` ("6 432 records were evaluated … 173 were checked in depth; 6 concern you"). Two real spike figures and one invented one in a single sentence shown to the owner. Chapter 11 otherwise describes the mockups truthfully (areas, phone navigation with four targets, three answers above the action bar at 390 × 844, contrasts, sizes, breakpoints all check out).

---

## D. Design weaknesses

### D1 · high · Tenant separation: "die eine Ausnahme" is at least ten, and the transaction model is undefined
**Where:** 09 §9.2; 04 §4.1, §4.7, §4.8; 03 §3.2 rule 2 and §3.4; 10 §10.2; ADR 003, ADR 012.
**Problem:**
- Cross-tenant system paths besides the candidate search: finding existing alerts for a changed or withdrawn entry across organizations (7.6); recomputation after a new rule set or prompt (7.6); due digests per rule; due deliveries and retries; entry merges that move alerts (6.2 rule 4); memberships of a user at login; accepting an invitation by token; retention and clean-up; organization export and deletion; instance metrics per organization. None can run with `app.org_id` set to one organization.
- User-scoped routes have no tenant: `/me/notifications`, `/me/preferences`, `/me/push-subscriptions`. Their tables carry `organization_id` (`notification`, `user_preference`) or do not (`push_subscription`). 9.2 says the tenant context "entsteht nur aus dieser Prüfung" of `/orgs/{orgId}`.
- 4.1 says "Jede Zeile trägt `organization_id`, auch Kindtabellen"; `rule_target` and `push_subscription` list none. `platform` is classified as global but holds `audit_event` with organization data served by `GET /orgs/{orgId}/audit`, and `job.payload`.
- "Der Mandant wird je Transaktion gesetzt" requires every unit of work, including every read, to run in an explicit transaction. With one DbContext per module, a request that touches three modules opens three connections and three transactions, and the outbox row in `platform` must share the transaction of the module that writes. How the contexts share one connection is not described. The research flags exactly this as needing a prototype.
- The cross-tenant test (2.2) checks the path parameter. The likelier leak is a foreign ID inside a body: `rule.project_ids`, `rule_target.recipient_ref`, a component's `project_id`.
**Fix:** Before spike 0.6, write the list of system operations and classify every table as tenant-scoped, user-scoped or global. Give user-scoped tables a policy on `app.user_id`. Give system fan-out a narrow, named mechanism (security-definer functions that return only IDs, or a separate role used by named jobs) and test each one. Define one unit of work with one connection and transaction per request or job that all module contexts join. Make the spike's exit criteria explicit: parallel tenants, pool reuse, multi-context transaction, read path overhead. Extend the automatic test to IDs in bodies.

### D2 · high · The model can suppress an alert wherever the version is unknown
**Where:** 07 §7.4 ("Schutzmassnahmen"), §7.5 (bullets under the threshold table); 09 §9.5, §9.7; 14 Phase 4 acceptance; 15 §15.1.
**Problem:** 7.4 claims: "Ein manipulierter Text, der «nicht betroffen» erzwingen will, scheitert daran." The floors apply only when the version is in range. When the version check says unknown, "Sein Urteil «not_affected» führt zu Stufe 1 nur mit hoher Sicherheit und einem zitierten Beleg". Confidence is reported by the model itself, and the cited source can be the manipulated text, since citations only have to be numbers from the input. An actively exploited critical vulnerability with an unknown version can therefore be set to level 1, which is hidden from the default list and never notifies. Unknown versions are common (B7). The Phase 4 test ("senken keine Stufe unter die Untergrenze") can only fail where a floor exists, so it does not test this path.
**Fix:** In rule set v1 the model never produces level 1 on its own: `not_affected` counts as `unlikely` (−1.5) and creates a proposal that a person confirms. Add floors that do not depend on the version verdict (strong match plus active exploitation: at least 3 when unknown, 4 when in range). Accept a citation for a downgrade only if it points to a structured field (vendor status "unaffected"), not to free text. Rewrite the Phase 4 test with unknown-version cases and assert the level against the rule-only result.

### D3 · medium · Database roles: API and worker are handed the owner's password
**Where:** 09 §9.2 ("verbindet sich als Rolle ohne Eigentum und ohne `BYPASSRLS`"); 12 §12.2 (`POSTGRES_USER: csh_migrator`, `env_file: .env` on all services), §12.4 (one variable `CSH_DB_PASSWORD`).
**Problem:** `POSTGRES_USER` is a superuser in the official image. The same `.env` with its password goes to `api` and `worker`. Where the password of `csh_app` comes from is not defined. As written, the processes that must not bypass RLS hold the credentials that do.
**Fix:** Two secrets (`CSH_DB_MIGRATOR_PASSWORD`, `CSH_DB_APP_PASSWORD`), generated by `init-env`; only `migrate` receives the first. State it in 12.4.

### D4 · medium · Notification state: a global unique key drops legitimate re-alerts and leaves bundling and digests undefined
**Where:** 08 §8.2, §8.4, §8.6; 04 §4.7.
**Problem:**
- The fingerprint contains alert, event kind, level, projects, exploitation, fix. A level that goes 4 → 5 → 4 → 5 produces the same fingerprint for the second rise. The recipient was told "lowered" and the renewed rise to Critical is discarded as already sent. EPSS moving around a class boundary can cause exactly this (D5).
- "Entwarnung … einmal, wenn zuvor dorthin gemeldet wurde" needs what was last sent to this recipient on this channel. No table holds it.
- Bundling "innerhalb von fünf Minuten": is a level 5 message held for five minutes, or sent at once and followed by a bundle? How does the bundle get a key?
- Digests: a held delivery has no state (`pending, sent, failed, dead, suppressed`), the empty weekly summary has no `alert_id`, and one digest e-mail covering thirty deliveries is not represented.
- `alert.is_backfill` sits on the alert. When a new project matches forty alerts that already exist for the organization, each gets `alert.newly_affected` and forty notifications follow; criterion 8.8 assumes all forty are new alerts.
**Fix:** Keep one row per (alert, recipient, channel) with the last notified state (level, projects, exploitation, fix) and compare against it. Key deliveries on the `alert_event` that caused them. Send level 5 at the leading edge and bundle followers. Add a `digest` table that references its deliveries. Put the backfill mark on the finding or on the triggering job.

### D5 · medium · Signals (EPSS, KEV) have no defined path through the data model
**Where:** 05 §5.1 and §5.5; 04 §4.4, §4.9; 06 §6.4; 14 Phase 1 acceptance ("ein Eintrag mit vier Belegen").
**Problem:** 5.1 says a signal is "Anreicherung bestehender Einträge". The model offers only source record → observation → `entry_source`, and the acceptance criterion counts EPSS as a "Beleg". Taken literally, the EPSS file is 407 166 source records that change daily, each with eleven kept revisions, a daily revision bump and an `entry_change` row for about 100 000 entries (tens of millions of history rows per year). 4.9 sizes only the CVE Program. EPSS also has no hysteresis: a value oscillating around 50 % flips the material fingerprint, the reassessment and possibly the level every day.
**Fix:** A separate small table for signals (source, CVE, value, observed at) without revisions; update the entry only when a class changes; no revision or history row for movement inside a class; a dead band at class boundaries. Reword the acceptance criterion.

### D6 · medium · The job system is larger and less specified than "rund 300 Zeilen"
**Where:** 02 §2.2; 03 §3.4; 15 §15.1 ("Kleiner Umfang").
**Problem:** 3.4 lists leases with a watchdog, wake-up by `NOTIFY`, backoff with jitter, dead letters with manual retry and an admin page, coalescing, concurrency limits per kind and per source, cron with time zone and leader election, an outbox with a dispatcher and subscriptions, clean-up, trace propagation and tenant context. That is a small framework. Missing from the description: lease renewal for long jobs (the 180-day backfill runs up to 60 minutes in one job), a time limit per job, a priority policy (a backfill of 100 000 entries shares the queue with a KEV-triggered alert), and retention for the outbox. The sentence "Mit einer fremden Job-Bibliothek entsteht … ein Fenster" contradicts the table above it, which says Quartz can enlist in the transaction and Wolverine is "beste verfügbare Lösung". The Quartz fallback "ohne dass Module es merken" would lose the transactional enqueue that modules rely on.
**Fix:** Keep the decision; it is defensible on licence and churn grounds. Bound the scope for the MVP: job table, `SKIP LOCKED`, heartbeat, one retry policy per kind, partial-index coalescing, two priority classes, Cronos schedules. Drop the separate outbox dispatcher for now and insert the subscribers' jobs in the same transaction through an in-process subscription registry. Budget it honestly (E1).

### D7 · medium · Closed findings are not reopened when the organization is affected again
**Where:** 06 §6.7 ("wird durch eine Neubewertung nicht wieder geöffnet, ausser die Stufe steigt").
**Problem:** Incomplete fixes are common. The vendor widens the affected range, the installed (upgraded) version is in range again, the level is the same as before, and the finding stays "remediated" silently.
**Fix:** Reopen also when the version verdict of a closed finding changes to `in_range`, or when the fixed version moves above the installed one; notify as `alert.newly_affected`.

### D8 · medium · SSRF through Web Push subscriptions, and unverified external recipients
**Where:** 09 §9.5 and §9.7 (SSRF protection "für Webhooks und Quellen"); 04 §4.7 (`push_subscription.endpoint`); 10 §10.2 (`POST /me/push-subscriptions`); 08 §8.3 ("externe E-Mail-Adressen").
**Problem:** Any signed-in user, including a Viewer, can register an arbitrary URL as push endpoint; the worker will POST to it. The egress guard is specified for webhooks only. Rules can send alert content to any e-mail address without confirmation; on an instance with open registration that is a relay for unsolicited mail, and `List-Unsubscribe` "führt zu den Einstellungen" that an external recipient cannot open.
**Fix:** Apply the same resolve-check-pin guard to push endpoints (optionally an allow-list of known push services plus admin-approved hosts). Confirm external addresses by a link before first use and give them a tokenised unsubscribe.

### D9 · medium · Provider SDK: three contract gaps before it is frozen in Phase 1
**Where:** 05 §5.2, §5.3; 13 §13.9.
**Problem:**
- Cursor commit: `FetchAsync` streams records and "schreibt den Fortschritt in ctx.Cursor". If the cursor advances before the core has stored the yielded records and the worker dies, records are lost. The "no gaps" criterion depends on this.
- One raw record gives one observation. A CSAF advisory with twelve CVEs has product status, scores and remediation per vulnerability. Whether a "Bezug" contributes affected products and fixes to each of the twelve entries is not said; for distributions that is the main value. A CVE record itself has CNA and ADP containers with different claims, so "Herkunft je Feld" must go below the source.
- "Adapter werden beim Start über ihre Assembly gefunden": the image is read-only without a shell, so a third-party adapter means a rebuilt image or an undocumented plug-in directory. Recommending AGPL for the application and Apache for the SDK "damit Dritte Quellenadapter unter eigener Lizenz schreiben können" needs an explicit plug-in permission or legal advice, since the adapter is loaded into the AGPL process.
**Fix:** Let the adapter yield checkpoints in the stream; the core stores cursor and preceding records in one transaction. Allow `Normalize` to return claims per referenced identifier. Decide the plug-in mechanism and the licence wording together (decision 15.2/E2).

### D10 · medium · Catalogue ownership and three matching ambiguities
**Where:** 03 §3.2 rule 3 ("`Intel` kennt niemanden"); 04 §4.5 (`assets.catalog_product`); 06 §6.5; 07 §7.2; 14 task 2.4 (depends on 1.7).
**Problem:** Normalisation in Intel resolves vendor spellings through the catalogue ("Katalogeinträge tragen Namensmuster je Hersteller; Versionslinie und Plattform werden aus dem Namen gelöst"), but the catalogue belongs to Assets, which Intel must not know. Matching: "Vendor ist optional; ein eindeutiger Produktname genügt" plus free entries invites collisions on generic names when the vendor is `n/a`. What key a free entry without vendor gets, and whether its match is strong, is not said. No rule covers product variants of one family (B2, seventh example).
**Fix:** Make the catalogue its own global module (or part of Intel) with a contract used by Assets and Intel. Allow product-only matches only for catalogue products marked unambiguous; free entries without vendor match weakly. Add a variant relation to the catalogue.

### D11 · medium · AI integration: configuration model, cost profile, stale text, language
**Where:** 07 §7.4, §7.6, §7.7, §7.9; 12 §12.4; 06 §6.4.
**Problem:**
- 7.7 needs several providers at once (a local and a cloud one, routed by the organization's policy, "auch nicht als Rückfall") and two model roles. 12.4 offers one `CSH_AI__PROVIDER`, one endpoint, one model.
- The model is called for every candidate that is not out of range ("gleich viele Aufrufe wie Kandidaten"), including level 2 and the whole lookback (C1).
- Reassessment is triggered by the material fingerprint only. A new workaround, a new vendor advisory or a rewritten description is not material, so "What to do" can stay outdated while the sources have moved.
- `what_happened` is written "in der Sprache der Organisation" and stored immutably; the interface language is per user and German arrives after 1.0. Template text without a model is English only.
**Fix:** Named provider entries with a flag local or cloud, and a mapping role → provider. Gate model calls (level 3 or higher, or on demand). Add an assessment-input fingerprint (remediation text, set of primary sources) that refreshes the text without notifying. Decide the language rule for stored assessments.

### D12 · medium · Entry merges cascade across tenants and modules, unspecified
**Where:** 06 §6.2 rule 4 ("Meldungen, Befunde und Verlauf wandern mit"); 04 §4.4 (`merged_into_id`), §4.6 (unique organization and entry); 14 task 1.5.
**Problem:** If an organization has alerts for both entries, moving them violates the unique constraint; two numbers, two statuses, two histories and their deliveries must be merged. Intel cannot do it (it knows nobody), so Assessment must react for all tenants. None of this is described. With the four MVP sources, two vulnerability entries never merge, because everything is keyed by CVE.
**Fix:** Keep `merged_into_id` and the redirect; move the alert merge to the stage that introduces GHSA and OSV and specify it there. Remove it from task 1.5.

### D13 · medium · Deployment and operation
**Where:** 12 §12.2, §12.5, §12.6; 09 §9.3; 01 §1.6.
**Problem:**
- Unattended updates are recommended ("für unbeaufsichtigte Updates geeignet, etwa per Cron") while "Rückwärts wird nicht migriert" and API and worker check at start "ob das Schema zu ihrer Version passt". A bad minor release then has no way back: the previous image refuses the newer schema and no backup was taken.
- First start: the port is bound to `127.0.0.1`, the cookie is `__Host-`… `Secure`, Web Push and the service worker need HTTPS. On a fresh server "in unter 3 Minuten … anmeldebereit" is only reachable through a tunnel or after the proxy with a domain is set up, which the criterion does not include.
- `read_only: true` without a `tmpfs`: .NET on a read-only root normally needs a writable `/tmp` or diagnostics switched off, and the full CVE import ("666 MB (gezippt)") needs scratch space. Spike 0.7 does not cover this.
- The backup command `docker compose exec db pg_dump -Fc csh > csh.dump` fails as written: no `-U csh_migrator` (the default role would be `root`) and no `-T` for binary output. The restore order ("Dump einspielen, `migrate` ausführen") restores grants to `csh_app` before the role exists.
- `postgres:18.7` is pinned to the patch level while the application floats on `1`; the database never receives security patches through `pull`.
**Fix:** `migrate` takes a dump before applying a migration that changes the schema (kept in a volume, last N), and the schema check accepts a newer minor schema within a major. Ship the Caddy example as part of the quick start and define the 3-minute criterion as "after images are pulled, via the bundled proxy or localhost". Add `tmpfs: /tmp` and a scratch volume for imports; test in 0.7. Correct the commands. Use `postgres:18`.

### D14 · medium · Data model gaps an implementer would have to guess
**Where:** 04 §4.4 to §4.8.
- Observation: 4.2 shows at most one current observation per source record; 3.3 says one per raw revision; 6.4 compares with "der vorigen Beobachtung"; 6.5 stores an extracted observation with "Herkunft «extrahiert»", for which there is no column.
- Where the four content kinds of 5.1 live (`source.kind` has two values, `security_entry.kind` two).
- Verdict without a model: how `version_verdict` maps to `finding.verdict`; where `uncertain` goes.
- How `alert.status` is derived for mixed finding states (only "offen, solange ein Befund offen ist").
- Organization settings (deadlines per level, AI policy, MFA requirement, time zone, language, e-mail detail level) have no columns; the rule set and curated corrections have no table although `migrate` loads them.
- `Idempotency-Key` (10.1) needs storage; none is modelled.
- Consecutive `alert.number` under concurrent workers: mechanism not named.
- From when a deadline runs (first raised or last level change) and whether it resets.
- Product default status and "unaffected" ranges of CVE records: `entry_affected.status` exists, the product-level default does not.
- Whether assignment is per alert while status is per project ("Take into review … setzt den Status, weist zu": for which findings?).
**Fix:** One page of decisions; most are a sentence each.

### D15 · low · LISTEN/NOTIFY and health checks
`LISTEN` needs a dedicated connection outside the pool per process and does not survive a transaction-mode pooler (the growth path in 3.7 should say so). The health checks start a new .NET process every 15 and 30 seconds; the research recommends a small native helper. The worker heartbeat must be per instance, otherwise a dead second worker looks healthy.

### D16 · low · Two framework details
`AddValidation` discovers types only in the assembly that calls it (backend research, section 2); with DTOs in module assemblies each module must register. 9.3 says "Angular sendet das Token von Haus aus mit"; that holds only if the server sets a readable `XSRF-TOKEN` cookie, while 10.2 returns the token in `GET /auth/session`, which needs an interceptor.

### D17 · low · Instance administrator
"sieht aber keine Daten fremder Organisationen" (9.1) cannot be enforced against someone with the database password and the master key, and the dead-job page shows payloads. Say "has no access through the application". There is one instance administrator with no way to add or transfer the role.

### D18 · low · Webhook payload
The webhook always carries the full alert in the form of 10.3, including component versions. E-mail has a terse form for confidentiality; webhooks should have the same choice per endpoint.

### D19 · low · Source content in several languages
BACS documents exist in four languages joined by a group ID. `security_entry` has one title, summary and description. Decide which language is stored and shown.

### D20 · low · The project never tells operators about its own security updates
"Die Instanz fragt nirgends nach neuen Versionen" (12.6) is right for privacy, but a security alert system should at least put itself into the catalogue so that an advisory for it reaches the instance administrator through the normal sources.

---

## E. Feasibility of phases and acceptance criteria

### E1 · high · 16 to 23 weeks is not credible for this scope, and the MVP is not lean
**Where:** README ("Umfang"); 14 (table and all phases); 02 §2.2 and §2.4; 15 §15.1 ("Umfang für ein kleines Team": Rest "mittel").
**Problem:** Clearly underestimated items:
- Phase 0 (2 to 3 weeks): repository and build chain with multi-architecture images and licence gate, host program, job system with outbox and schedules, tenant context, audit, settings, secret encryption, telemetry, two spikes with ADRs, the whole of Identity with TOTP, CSRF and rate limiting, the Angular workspace with token file, header, navigation and sign-in pages, the OpenAPI pipeline, architecture tests.
- Task 2.4: a catalogue of "rund 300 Produkten mit Aliassen und CPE-Zuordnung", name patterns and platform relations. The spike's hand-written catalogue has eleven entries.
- Task 3.5: a reference set of 200 labelled pairs of entry and project, inside the same 3 to 4 weeks as matching, rule set, alerts and three complete pages with status workflow and live updates.
- The component library: "15 bis 20 kleine Komponenten" each with seven states and a state page for visual regression (13.4, 11.7).
- Phase 6 (2 to 3 weeks) contains new features, not hardening: eight maintenance commands including key rotation, organization export and deletion and full import, `doctor`, self-monitoring, plus security pass, manual accessibility audit, load test, restore and update rehearsal, all documentation, signed releases and an own instance.
- No allowance for owner approvals (mockups in 2.6, 5.8, decision 15.2/E15), the BACS answer, or the "unabhängige Sicherheitsprüfung vor 1.0" that 15.1 promises and no phase contains.
The brief asks for a deliberately lean MVP. The plan's MVP includes RLS, a custom job framework, per-field provenance with adjustable trust ranks, entry merging, two model roles, four channels with bundling and precedence, `Idempotency-Key` and `If-Match` conventions, SBOM and signed images, and visual regression tests.
**Fix:** Either cut or re-estimate; my expectation for the scope as written is roughly one and a half to two times the stated range. Candidates to move behind 1.0 without hurting the core: AI extraction from free text (4.5), corrections as model context (4.7), entry merge (D12), adjustable trust ranks and the review list UI, `Idempotency-Key` and `If-Match`, key rotation and organization export as commands, anomaly halt (9.5), signed images. Make RLS a result of the spike rather than a premise. Start the catalogue with the products of the reference projects plus the top vendors of the spike week and grow it from the "unknown product" metric.

### E2 · medium · Acceptance criteria that are tautological, unmeasurable or missing
**Where:** 01 §1.6; 14 (all phases); 08 §8.8.
- "keine übersehene Betroffenheit bei exaktem Produkt- und Versionstreffer" (1.6, Phase 3): an exact match is what the matcher finds by definition. Misses come from aliases, version schemes and missing catalogue entries, which the wording excludes.
- "Anteil unbegründeter Meldungen ab Stufe 3 unter 20 %": no definition of "unbegründet", no rule for who labels and how the 200 pairs are selected. The expected level in the reference set is defined by the rule set under test.
- Phase 0: "10 000 eingereihte Jobs werden von zwei Workern genau einmal verarbeitet; ein während der Arbeit beendeter Worker verliert keinen Job." The second half implies the first cannot hold; the achievable property is at least once with idempotent effect.
- Phase 4: the injection test cannot fail where no floor exists (D2).
- Phase 2: "in unter 5 Minuten" by whom, how often; Phase 0 and 1.6: "in unter 3 Minuten" on which hardware, with or without image pull and TLS (D13).
- 1.6 and Phase 3: the three answers above the action bar at 390 × 844 hold for the sample text (I checked the screenshot); with longer titles, German, or model output they depend on length limits that are not defined.
- Phase 5 accepts by 8.8 only: nothing for Web Push including the iOS installation flow, digests with time zone and the empty summary, precedence, bundling, the hourly cap, or the SSRF guard.
- 6.4: "Update von der Vorversion" has no previous version before 1.0 unless pre-releases are tagged.
- 1.6: catching up after 30 days is only tested for 48 hours in Phase 1.
**Fix:** Define the reference set (composition, blind labelling by a second person, what counts as unfounded) and add a recall criterion over spelling and version-scheme variants. Reword the job criterion. Add criteria per channel. Set character budgets for the three answers. Tag 0.x releases from Phase 1 on.

### E3 · medium · Dependencies that are missing or contradict the diagram
**Where:** 14 (diagram, "Die Phasen 1 und 2 sind voneinander unabhängig", task tables).
- Task 2.4 depends on 1.7, so 2.5, 2.7 and 2.8 depend on Phase 1. The phases are not independent.
- Invitations (2.1) and password reset (in the API, in no task) need mail sending, which arrives in 5.4. SMTP is optional (12.4); what happens without it is not said.
- Task 1.9 and the Phase 1 acceptance depend on BACS (A1).
- Task 2.6 has an approval gate; 5.8 combines mockup and pages without one; decision 15.2/E15 promises mockups for source status and the quiet bulletin "vor ihrer jeweiligen Phase", but 1.10 and 3.6 have no mockup task.
- Phase 6 has no dependency column, and task 1.11 and 6.5 both deliver the maintenance commands.
**Fix:** Correct the diagram, move a minimal mail sender to Phase 0 or 2, add the mockup tasks with their gates.

### E4 · medium · Specified in the chapters, absent from every phase
**Where:** 05 §5.5 and §5.8 (daily contract test in operation); 06 §6.8 and 10 §10.2 (review list, `/instance/quality`); 09 §9.3 (list of common passwords, MFA requirement per organization, session list), §9.4 (organization export and deletion), §9.5 (halt on mass changes, with no thresholds given); 08 §8.3 and §8.5 (muting, terse e-mail form); 10 §10.1 (`Idempotency-Key`, `ETag`); 11 §11.4 (Team and Settings pages, audit view), §11.7 (server-side translation of messages); 01 §1.5 (installable PWA: manifest); 15 §15.1 (independent security review).
**Fix:** Either add tasks or mark each item as after 1.0. This list is also the quickest source of scope cuts for E1.

### E5 · low · Waiting time is not in the estimate
Owner approvals for at least four mockup rounds, the BACS answer and an external security review are calendar time outside the "eine Person" estimate. State calendar time separately from effort.

---

## F. Language

No "ß" found in the plan or the ADR register. Swiss spelling is consistent.

### F1 · medium · Five key words carry several meanings; there is no glossary
**Where:** throughout, worst in 06 §6.4, 07, 08.
- **Stufe:** priority level 1 to 5; funnel stage 0 to 4 (7.1, headings "Kandidatensuche (Stufe 1)", "Das Regelwerk für die Stufe (Stufe 4)"); change-detection stage 1 to 5 (6.4); escalation step (8.7); "Ausbaustufe 1". "Stufe 1" alone can mean Not relevant, candidate search, transport check or the first expansion.
- **Meldung:** the alert of an organization (4.6) and a source's advisory or entry ("Sicherheitsmeldungen" in the README, "wenige Meldungen pro Tag" in 7.9, "einmal je Meldung" in task 4.5, "312 Meldungen zu WordPress-Plugins" in 7.2).
- **Bulletin:** the overview page and the entry kind for warnings without an identifier.
- **Zusammenfassung / zusammenfassen:** digest (8), job coalescing (3.4, task 0.4), summary.
- **Rückzug:** backoff (2.2, task 1.1) and withdrawal of a CVE (9.5); "Rücknahme" is undo (3.9).
**Fix:** Reserve "Stufe" for the priority; call the funnel "Schritt" and the change detection "Ebene". Rename the entry kind (for example "Warnung"). Add a glossary German to English (Eintrag/entry, Meldung/alert, Befund/finding, Einschätzung/assessment, Beobachtung/observation, Benachrichtigung/notification), since code and interface are English.

### F2 · low · Grammar and wording
- 12 §12.3: "Kein Shell" → "Keine Shell".
- 02 §2.2: "Kein Outbox" → "Keine Outbox".
- 09 §9.4: "Kein Heimtelefon" is a literal translation and not understandable; for example "Keine Rückmeldung an das Projekt".
- 01 §1.5: "Kanal-Trio" (B3).

### F3 · low · Naming
`security_entry.primary_key` (4.4) is the business identifier, not the primary key; the API calls it `key`. Rename the column.

---

## Appendix 1 · Recomputation of the worked examples in 7.5

| Entry | Severity | Exploit. | Affected | Critic. | Exposure | EPSS | Sum | Level by table | Plan |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Puppet Enterprise | 4 | 3 | 1 | 1 | −0.5 | 0 | 8.5 | 5 (also floor 5) | 5 |
| PHP | 4 | 3 | 1 | 0 | 1 | 0 | 9 | 5 (also floor 5) | 5 |
| PostgreSQL | 3 | 0 | 1 | 1 | −0.5 | 0 | 4.5 | 4 (on the threshold) | 4 |
| MySQL Server | 4 | 0 | 1 | 0 | −0.5 | 0 | 4.5 | 4 (on the threshold) | 4 |
| Umbraco CMS | 2 | 0 | 1 | 0 | 1 | 0 | 4 | 3 | 3 |
| ingress-nginx | 1 | 0 | 1 | 1 | −0.5 | 0 | 2.5 | 2 | 2 |
| SQL Server | no rule applies (B2) | | | | | | | | 1 |

The factors agree with the mockup data (Platform Ops business-critical and internal; Webshop normal and internet-facing, its database internal; Customer Portal normal and internet-facing). The mockup history "First assessed at level 4" for Puppet on 3 Nov also checks out (4 + 0 + 1 + 1 − 0.5 = 5.5). The plan writes five summands per row and leaves out EPSS without saying it is 0.

Range of the sum: −0.5 (low, no exploit, in range, low criticality, isolated) to 11 (critical, active, in range, high, internet, EPSS above 50 %).

## Appendix 2 · Spot checks against the evidence files

**Match (no action):** 6 432 published records and 6 452 in total · 173 candidates, 2.7 % · 590 per day and peak 2 077 (spike README) · PHP 295 of 297 · WordPress 477, 7.4 %, no core record · 97 %, 74 %, 88 %, 84 % in 5.5 (97.0, 74.4, 87.5, 83.6 in the spike) · CPE 39.5 % · version types 14 569, 5 320, 2 827, 2 238, 316 · 3.6 kB median · half a million tokens per week (173 × 1 800 to 3 600) · .NET 10.0.15 and end of support 14.11.2028 for 10 and 11 · Npgsql provider 10.0.3, version 11 not released · PostgreSQL 18.7, supported to November 2030, new volume path · Cronos 0.13 · Microsoft.Extensions.AI 11.0.0, released May 2025 · MailKit 5.0.0 · Lib.Net.Http.WebPush 3.3.1, MIT · OpenTelemetry 1.21.0 · Quartz.NET 4.0 on 3.9.2026 with four schema migrations (4.2, 4.3, 4.6, 4.7), 3.22 line · Wolverine recurring schedules since 6.34 · Coravel last release January 2025 · TickerQ relicensed with certificate · OllamaSharp 5.5.2 · Anthropic 13.10.0, Apache-2.0 · Google.GenAI 2.2.0 · MediatR 15, AutoMapper 17, MassTransit 9, FluentAssertions 8, ImageSharp 4 · Angular 22.2, active to June 2027, LTS to June 2028, yearly majors, TypeScript 6.0 · CDK and Aria 22.3, Aria stable since 22 · TanStack Table 9.3 · TanStack Query stable since 17.11.2026 · orval 8.48 · Transloco 8.4 · Capacitor 8.6, 9.0 as release candidate · spartan/ui 1.0 in June 2026, needs Tailwind 4 · PrimeNG commercial from 22, last free line Angular 21 only · EN 301 549 v4.1.1 of September 2026 · iOS 16.4 home-screen condition · KEV 1 782 entries, CC0, ETag ignored and `If-Modified-Since` honoured · EPSS file 2.85 MB, 407 166 values · CERT-Bund ROLIE 49.5 MB · OSV 48 ecosystems · NVD window 120 days, 5 % with CPE after eight days, change of practice in April 2026 · GitHub 60 requests per hour without token · BACS relaunch on 20.8.2026, 3 of 12 with CVE, 7 of 12 phishing, no validators · CISA feeds 403, GitHub mirror works · BSI list with 14 providers · Red Hat list 2 to 4 MB and no ETag · Ubuntu feed with ten items · source counts 4 + 11 + 21 + 18 = 54, 13 through the CSAF reader · contrast ratios 15.8, 8.2, 7.0, 5.4 to 12.9 (recomputed: 15.83, 8.16, 6.99, 5.39 to 12.93) · effort sum 16 to 23 weeks.

**Mismatch or not in the evidence:** see C1 to C5.

## Appendix 3 · How the level histogram in C1 was produced

`python3 -I levels.py <repo>/prototypes/relevance-funnel/funnel.py <week data>` imports the spike's `load_latest`, `facts` and `match`, takes every structured candidate (173 pairs of entry and project, 173 distinct entries), maps the CVSS score to the severity class, the SSVC or KEV state to the exploitation factor, and applies the thresholds and floors of 7.5. EPSS is taken as 0 and criticality as normal for all; affectedness and exposure are set uniformly per scenario. It is a bound, not a forecast: real profiles mix the scenarios, and out-of-range versions (the spike reports 19 for three products) drop to level 1.
