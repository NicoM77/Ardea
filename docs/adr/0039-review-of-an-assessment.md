# ADR 039: Review of an assessment by a person

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 035 (actions, people's decisions), ADR 037 (assessment by a language model), ADR 029 (system functions)
- Master plan: 7.5, 7.6, 7.7, 7.9, 11.4, 14 (phase 4), task 4.7

## Context

Master plan 7.5 makes the model's «not affected» a proposal a person confirms; 7.6 lets the model lower a level by at
most one «solange kein Mensch das Urteil des Modells bestätigt hat»; 7.7 reassesses «dieser Befund» when a person
confirms, discards or corrects an assessment; 7.9 records a wrong verdict or a wrong level with a reason and makes
the correction rate per model and prompt version a figure of the administration. Open were the shape of the request,
what a corrected level does, how a person's «not affected» behaves when the level rises later, where the figure
lives, and how the pages learn that the model has answered.

## Decision

1. **One request per review.** `POST /orgs/{orgId}/findings/{findingId}/review` (members and up) takes `decision`
   (`confirm` or `correct`), `expectedVerdict` and `expectedSource` (what the caller saw), and for a correction
   `verdict`, `level` or both with a `reason` (required, at most 2 000 characters). It answers the finding as it is
   after the reassessment; `400 invalid_review`, `404 finding_not_found`, `409 verdict_conflict` (the verdict or its
   source changed meanwhile), `409 finding_closed`, `409 nothing_to_confirm` (only a verdict of the model can be
   confirmed).
2. **Locks and one unit of work.** The command takes the alert's advisory lock and then the component's
   (`AlertAssessor.LockAsync`, `Remediations.LockAsync`), the order the jobs take them in, reads the finding again and
   compares the expected verdict under both. Feedback row, history row, the person's verdict, the reassessment of this
   finding (`AssessmentScope.ForComponent`), the actions' sync and the audit entry are one unit of work; the answer is
   read before the commit.
3. **What a confirmation does.** The model's verdict becomes a person's (`verdict_source` `human`). The rules then
   take it as `HumanVerdict`, the model's verdict no longer, so the floor «at most one level below the result without
   the model» no longer applies and «affected» or «likely» also lift the cap of 4 (master plan 7.6 step 3). A
   confirmed «not affected» is a person's exclusion: the finding closes as `not_affected` by that person, level 1.
4. **What a correction does.** Another verdict becomes the person's verdict as above. A level the person expects is
   **feedback**: it is stored and counted, and the rules keep setting the level. A person's level that overrode the
   rules would outlive the inputs it was given for and could undercut the floors for exploited flaws; who disagrees
   with a level changes the verdict, the profile (criticality, exposure, protection) or the deadline.
5. **A person's «not affected» later.** When a reassessment asks whether a closing reopens (master plan 6.7), a
   person's «not affected» counts like «unlikely», as the model's does, so a confirmed proposal stays closed until
   something beyond the verdict raises the level. Should the system reopen it, the verdict stays the person's and
   still counts like «unlikely». When a person takes the closing back, the verdict returns to the rules, and the model
   may be asked again.
6. **Feedback.** `assess.assessment_feedback` (organisation data, row-level security, tenant filter) holds one row per
   review: finding, alert, the model's assessment whose verdict was reviewed (only when the verdict came from the
   model), `confirmed` or `corrected`, the verdict's source, the verdict before and after, the level before, the
   expected level, the reason, the person and the time. History rows: `finding.verdict_confirmed` and
   `finding.verdict_corrected` (history only, no notification event); the finding view carries the latest review.
7. **The figure.** `GET /instance/ai/quality` (instance administrator) lists per provider, model and prompt version
   the checked assessments, their verdicts, the confirmed and corrected reviews and the correction rate (corrected
   among the reviewed). It reads through the new system function `assess.model_feedback_counts()` (ADR 029): counts
   only, no texts, no ids. A dedicated endpoint rather than metrics in the instance status, because the
   administration shows it in a table and the status metrics are flat names.
8. **Live.** Inserts into `assess.assessment` and changes of `assess.finding` signal the organisation like history
   rows do, so a page reloads when the model's answer or a verdict arrives without a change of level.
9. **Web.** The model's texts are interpolated as text, never as markup or links; links come from the numbered
   source list Ardea built. «Confirm the estimate» and «Correct this assessment» sit with the finding (action page)
   and under the component's factors (alert page).

## Alternatives

- **A person's level as an override.** Rejected, see 4.
- **Confirming through the finding's status only.** Closing as «not affected» already excludes a finding, but it
  records nothing for the model's quality and cannot confirm «unlikely» or correct a verdict.
- **Reassessing through a queued job.** The page would show the old level until the worker ran; the reassessment of
  one finding is cheap and belongs to the same unit of work as the person's verdict.
- **Correction rate among all verdicts.** Most verdicts are never reviewed, so the figure would mostly say how much
  people look; it is shown as counts beside the rate instead.

## Consequences

- A review of the model's verdict counts for the assessment that is the alert's current one at the time.
- A person's verdict is kept across reassessments and new assessments by the model until a person changes it or takes
  a closing back; the version check still decides where it can (in range, outside).
- The rate needs reviews to say anything; until then the administration shows a dash.
