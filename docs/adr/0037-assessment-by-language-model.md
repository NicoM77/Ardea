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
2. **Input.** The system message holds the rules; the user message one data block between `<source_data>` tags
   with the entry (title, description cut to 4 000 characters, affected products with ranges, severity with
   vector, exploitation, EPSS class, authority warning, solutions and workarounds), the hit components (open
   findings with a strong match) as `C1`, `C2`, … with version, version check, exposure, criticality, protection
   and its note, their projects as `P1`, `P2`, … and a numbered source list (1 = the record, then up to ten
   references). The block is JSON with `<` and `>` escaped, so no source text can close it; project names are
   replaced in every profile text. Component ids, project names and other components never leave the system.
3. **Check.** An answer is refused when it does not parse into the schema (unknown fields, values outside the
   lists, missing fields), when a component or source reference is not in the input, when a component has no or
   two verdicts, when a step has neither source nor `own`, when a text breaks the limits of 7.5 (240, 140, 120;
   300 for the other sentences) or a list is too long, or when a text contains an address, markup or an e-mail
   address: links come from the source list only. The check names the rule in the log, never the text.
4. **Two attempts, allowed providers only.** The providers come from `AiDataPolicies.Allowed`: local ones first,
   cloud ones only under `cloud_allowed`. A refused answer is asked again once of the same provider; a provider
   that fails or times out gives way to the next allowed one. After two attempts the rules stand.
5. **What is stored.** Every attempt that reached a provider is a row in `assess.assessment`, immutable (a
   trigger refuses updates): `engine` `rules+model` with the checked output (real component ids, the source
   list), or `rules` with `{note: without_model, reason, attempts}` and no text of the model. Both carry
   fingerprint, entry revision, rule set and prompt version, provider, model, language, tokens, duration, the
   person who asked (`requested_by`, empty for the system) and the open actions covered (`remediation_ids`).
   `alert.current_assessment_id` points to the latest; a failed one gives the note «ohne KI-Einschätzung».
   Additions to the columns of 4.6: `provider`, `requested_by`, `remediation_ids`.
6. **The verdict as an input of the rules.** `AssessAlert` reads the verdicts of the alert's current assessment
   when it was made for the entry's current revision, and passes a component's verdict to the rule engine only
   when its version check says *unknown* and no person gave a verdict (task 4.5). The finding then carries the
   verdict with source `model`; `not_affected` counts like `unlikely`, is shown as a proposal, and the finding
   stays open. The engine's floor keeps the level at most one below the result without the model, which it
   stores as `levelWithoutModel` in the factors. After storing an answer the job assesses the alert again.
7. **When.** After the rules, `AssessAlert` queues `EstimateAlert` when a finding that is not part of a first
   fill reaches level 4 or 5 by the rules alone, or 3 with an unknown version, every action of the alert's hits
   has fewer than five system assessments in seven days, and the organisation's policy allows a provider. The
   reverse search never queues one, so a first fill never asks the model. The job checks again, skips when the
   current assessment has the same fingerprint, and skips when the organisation's or the instance's tokens of
   the UTC day are used up. A person asks with `POST /orgs/{orgId}/alerts/{number}/assessment` (members and up):
   no threshold, no action quota, the token quotas still hold.
8. **Order of waiting jobs.** Jobs are claimed per kind ordered by `priority`; `EstimateAlert` uses the value
   `6 − level` (level 5 first) and 0 for a person's request. The values rank this kind only.
9. **Limits.** `ARDEA_AI__PARALLELISM` (default 1) is the job kind's concurrency per worker,
   `ARDEA_AI__CALL_TIMEOUT_SECONDS` (120) the limit of one call, `ARDEA_AI__DAILY_TOKENS` (2 000 000) and
   `ARDEA_AI__DAILY_TOKENS_PER_ORGANIZATION` (100 000) the daily quotas. Failed attempts count against them.
10. **New system function** (ADR 029): `assess.model_tokens_since(since)` returns the tokens all organisations
    used since a point in time, a sum only, for the instance quota.
11. **Telemetry.** The meter `Ardea.Assessment.Ai` counts calls by outcome, tokens by direction, call duration,
    errors, retries and assessments by outcome; the providers' clients keep their OpenTelemetry wrapper without
    sensitive data. Logs carry ids, provider, check rule, tokens and duration, never prompt or answer.
12. **Answers.** `AnswersView` gains `model` (the stored assessment in the organisation's language, with
    verdicts per component, a `proposal` flag for «not affected», steps with source numbers and the source list)
    and `modelNote` (`without_model`). `origin` is `model` when the lead alert has a current assessment for its
    entry revision; the structured parts (version, ranges, target version) stay as the data gives them.

## Alternatives

- **Re-using the rules' verdict as the model's field.** Storing only the model's verdict on the finding would
  lose what the model said for in-range components and make the level not recomputable; the full output is kept.
- **Counting the per-action quota through findings.** Joining assessments to actions through findings would count
  assessments of alerts whose findings joined an action later; `remediation_ids` records what was covered.
- **A token ledger table.** A separate usage table would duplicate the assessment rows; failed attempts are stored
  anyway, so the rows are the ledger.

## Consequences

- With parallelism above 1 two jobs for the same action may both pass the quota check before either writes; an
  action can then get one extra assessment per parallel slot.
- An assessment made for an older entry revision is not used for verdicts or answers; the next `AssessAlert`
  above the threshold asks again within the quota.
- Open for later: «Einschätzung korrigieren» (task 4.7) adds rows to `assessment_feedback` and a person's verdict,
  which already wins over the model's; the web interface for the request and the model's answers; catalogue
  learning (4.9, 4.10) reuses `Estimator` with its own prompt and quota.
