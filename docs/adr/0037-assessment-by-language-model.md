# ADR 037: Assessment by a language model

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 007 (level from a versioned rule set), ADR 023 (read, call out, write), ADR 029 (system functions), ADR 035 (actions)
- Master plan: 4.6, 4.9, 7.5, 7.6, 7.8, 7.9, 14 (phase 4), tasks 4.2 to 4.6 and 4.8

## Context

Master plan 7.5 and 7.8 fix what the model is asked, what it answers, when it runs, which data it sees and how
its answer is guarded; 7.6 fixes what its verdict may change. Open were the shape of the prompt and its version,
how an answer is checked, how a failure and the quotas are recorded, how the verdict reaches the rules, the order
of waiting jobs, and how a person's request differs from the system's.

## Decision

1. **Prompt and schema.** The prompt is a Markdown file compiled into the module
   (`Estimating/Prompts/estimate-{version}.md`); a changed prompt is a new file with a new version. The answer is
   the record `EstimateAnswer` with the fields of 7.5 in snake_case (`verdicts`, `likelihood`, `confidence`,
   `what_happened`, `preconditions`, `impact`, `actions` with `sources` or `own`, `unknowns`, `reasoning` with
   source numbers and component references). It is requested with `GetResponseAsync<T>` and the JSON schema
   response format, which Ollama, OpenAI-compatible servers and Anthropic enforce where they can.
2. **Input: no free text of the profile.** The system message holds the rules; the user message one data block
   between `<source_data>` tags with the entry (title, description cut to 4 000 characters, affected products with
   ranges, severity with vector, exploitation, EPSS class, authority warning, solutions and workarounds), a
   numbered source list (1 = the record, then up to ten references) and the hit components (open findings with a
   strong match) as `C1`, `C2`, … in projects `P1`, `P2`, …. Of a component only fixed values go out: the keys it
   shares with the entry (`product postgresql`, `purl pkg:npm/lodash`), kind, version, version check, packaging,
   exposure, criticality, protection (`none` or `effective`) and scope. The component's own name, its package
   coordinate, the protection note and every project or organisation name stay in the system: free text can hold
   names, people and other projects in any spelling, which no replacement catches reliably. A shared key is what
   the public record names; a scoped package such as `@acme/widget` goes out only when the record itself names it.
   The block is JSON with `<` and `>` escaped, so no source text can close it.
3. **Check.** An answer is refused when it does not parse into the schema (unknown fields, missing fields), when a
   value of a fixed list is not exactly its snake_case name (the enum converter alone would take `Affected` or
   `uncertain, likely`), when a component or source reference is not in the input, when a component has no or two
   verdicts, when a step has neither source nor `own`, when a text breaks the limits of 7.5 (240, 140, 120; 300 for
   the other sentences) or a list is too long, or when a text contains an address, markup or an e-mail address:
   links come from the source list only. Bidirectional control characters (U+061C, U+200E/F, U+202A to U+202E,
   U+2066 to U+2069) are removed before the check; whatever else could mislead a reader (`evil[.]com`) is for the
   web interface to render as plain text. The check names the rule in the log, never the text.
4. **Two attempts, allowed providers only.** The providers come from `AiDataPolicies.Allowed`: local ones first,
   cloud ones only under `cloud_allowed`. A refused answer is asked again once of the same provider; a provider
   that fails or times out gives way to the next allowed one. After two attempts the rules stand. The job kind has
   one attempt and a repeated run asks nothing, so a crash after the call never leads to further calls.
5. **What is stored.** Every job that reached a provider writes one row in `assess.assessment`, immutable (a
   trigger refuses updates): `engine` `rules+model` with the checked output (real component ids, the component's
   context per verdict, the source list), or `rules` with `{note: without_model, reason, attempts}` and no text of
   the model. Both carry fingerprint, entry revision, rule set and prompt version, provider, model, language,
   `attempts` (calls made), tokens, duration, the person who asked (`requested_by`, empty for the system) and the
   open actions covered (`remediation_ids`). A call without a usage report (failure, timeout) is charged its
   estimate: prompt characters / 4 plus the maximum output tokens. `alert.current_assessment_id` points to the
   latest; a failed one gives the note «ohne KI-Einschätzung». The write after the call is this row only; the
   reassessment that takes the verdicts in is queued as `AssessAlert`, so its failure cannot lose the row or its
   tokens. Additions to the columns of 4.6: `provider`, `attempts`, `requested_by`, `remediation_ids`.
6. **The verdict as an input of the rules.** `AssessAlert` reads the verdicts of the alert's current assessment
   when it was made for the entry's current revision, and passes a component's verdict to the rule engine only
   when its version check says *unknown*, no person gave a verdict, and the component's context (version, version
   check, packaging, exposure, criticality, protection, scope) is still the one the model saw (task 4.5). The
   finding then carries the verdict with source `model`; `not_affected` counts like `unlikely`, is shown as a
   proposal, and the finding stays open. The engine's floor keeps the level at most one below the result without
   the model, which it stores as `levelWithoutModel` in the factors.
7. **When.** After the rules, `AssessAlert` queues `EstimateAlert` when a finding that is not part of a first
   fill reaches level 4 or 5 by the rules alone, or 3 with an unknown version, every action of the alert's hits
   has calls left in its quota, and the organisation's policy allows a provider. The quota counts **calls**: five
   per action in seven days, retries included, and a job makes no more calls than are left; so the spike week's
   «höchstens 40 Aufrufe» holds for calls, not only for assessments. The reverse search never queues one, so a
   first fill never asks the model. The job checks again and skips when the current assessment has the same
   fingerprint (the system does not repeat a failed input either), and when the organisation's or the instance's
   tokens of the UTC day are used up. The fingerprint covers entry and revision, the components' contexts and the
   prompt version, not the rule set's version: a new rule set recomputes levels without asking the model again.
8. **People's requests.** `POST /orgs/{orgId}/alerts/{number}/assessment` (members and up) queues a job with a
   coalesce key of its own, so a waiting job of the system never swallows it; it needs no threshold and no action
   quota and may retry an input that failed, but it is bound by the token quotas and by
   `ARDEA_AI__DAILY_REQUESTS_PER_ORGANIZATION` (20): beyond it the endpoint answers `429` and the job asks nothing.
   People's jobs run before the system's in every organisation; the cap bounds what one organisation can put
   ahead of the others' work to 20 jobs a day.
9. **Order of waiting jobs.** Jobs are claimed per kind ordered by `priority`; `EstimateAlert` uses the value
   `6 − level` (level 5 first) and 0 for a person's request. The values rank this kind only.
10. **Limits.** `ARDEA_AI__PARALLELISM` (default 1) is the job kind's concurrency per worker,
    `ARDEA_AI__CALL_TIMEOUT_SECONDS` (120) the limit of one call, `ARDEA_AI__DAILY_TOKENS` (2 000 000) and
    `ARDEA_AI__DAILY_TOKENS_PER_ORGANIZATION` (100 000) the daily token quotas.
11. **New system function** (ADR 029): `assess.model_tokens_exhausted(since, quota)` says whether all
    organisations together used a quota since a point in time. It returns a flag, not a sum, so it tells a caller
    nothing about other organisations' activity beyond what the refusal of an assessment shows anyway.
12. **Telemetry.** The meter `Ardea.Assessment.Ai` counts calls by outcome, tokens by direction, call duration,
    errors, retries and assessments by outcome; the providers' clients keep their OpenTelemetry wrapper without
    sensitive data. Logs carry ids, provider, check rule, tokens and duration, never prompt or answer.
13. **Answers.** `AnswersView` gains `model` (the stored assessment in the organisation's language, with
    verdicts per component, a `proposal` flag for «not affected», steps with source numbers and the source list)
    and `modelNote` (`without_model`). `origin` is `model` when the lead alert has a current assessment for its
    entry revision; the structured parts (version, ranges, target version) stay as the data gives them.

## Alternatives

- **Replacing names in the profile's free text.** Project names, organisation names, people and addresses come in
  any spelling; a replacement list leaks what it does not know. Sending no free text is the only reliable rule.
- **Counting the per-action quota through findings.** Joining assessments to actions through findings would count
  assessments of alerts whose findings joined an action later; `remediation_ids` records what was covered.
- **Counting assessments instead of calls.** With one retry, five assessments can be ten calls; the plan's bound
  is on calls.
- **A token ledger table.** A separate usage table would duplicate the assessment rows; failed attempts are stored
  anyway, so the rows are the ledger.

## Consequences

- With parallelism above 1 two jobs for the same action may both pass the quota check before either writes; an
  action can then get up to two extra calls per parallel slot.
- An assessment made for an older entry revision or another component context is not used for verdicts; the next
  `AssessAlert` above the threshold asks again within the quota.
- The model knows less about protections than a person: «effective» without the note. A person's verdict, which
  wins over the model's, is the place for what the note says.
- Open for later: «Einschätzung korrigieren» (task 4.7) adds rows to `assessment_feedback` and a person's verdict;
  the web interface for the request and the model's answers, which renders model texts as plain text; catalogue
  learning (4.9, 4.10) reuses `Estimator` with its own prompt and quota.
