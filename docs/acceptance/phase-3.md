# Phase 3 acceptance: matching, rules, actions and the overview

Master plan chapter 14, phase 3. Criteria are checked by fast tests in the pipeline or observed once on a
real instance. Observed on 19.11.2026 on the acceptance instance of phases 1 and 2 (2 vCPU, 4 GB) after an
update to the image built from `main` with tasks 3.1 to 3.11.

## Reference set (task 3.7, ADR 036)

250 pairs from 244 real CVE records of the spike week, selected by `tools/reference-set/select_pairs.py`
and labelled by `data/reference/LABELLING.md`, run through the whole pipeline in the build
(`ReferenceSetTests`, about 30 seconds):

| Measure | Value | Threshold |
|---|---:|---:|
| missed among exact hits | 0 | 0 |
| missed | 0 of 67 (0 %) | at most 2 % |
| unfounded | 0 of 117 (0 %) | at most 20 % |
| pairs above every range, affected / not affected | 22 / 44 | at least 15 each |
| version check «unknown» | 34 % | measured |
| level as labelled / within one | 239 / 249 of 250 | calibration |

The first run missed 13 affected pairs. None of them was a matter of weights; all were gaps in reading
versions (ADR 033 decision 11). Rule set version 1 keeps its weights. The labels were made by a language
model from the records alone; the owner's review of them is open (below).

## Observed: the spike week and the three repositories

**Spike week** (test data, `ReferenceSetTests`): 1 812 of the week's 6 452 records against projects A, B and
C of the requirements, with the versions installed before that week's updates: 6 open actions in one
organisation (PHP level 5; .NET, MySQL, Linux kernel and PostgreSQL level 4; Umbraco level 3). The full week
gave the same actions.

**Three repositories of phase 2** (master plan 16.8). After the update, the worker read the stored CVE
records again with parser version 2 (77 425 records, 10 minutes, none quarantined); the project's reverse
search over 180 days then ran as a first fill in under a minute:

| | Value |
|---|---:|
| Components (actions/checkout, actions/setup-node, expressjs/express) | 1 109 |
| Findings, all marked first fill | 508 |
| of them strong hits: in range / unknown / out of range | 63 / 21 / 272 |
| Open actions | **34** |
| by level: high / medium / low | 4 / 5 / 25 |
| History | one `backfill.completed` («34 actions, highest level 4»), no `action.raised` |

The four at level 4 are `undici` 6.27.0 (11 findings) and 6.29.0 (5), and `qs` and `proxy-addr` without a
version (express has no lock file). 21 of the 34 are development-only packages or packages of unknown
scope, often the same package in several versions (`picomatch` five times, `brace-expansion` three times).
34 is far above the spike's 6 to 8: as 16.8 says, the owner decides whether transitive packages are bundled
(open point below).

## Criteria

| Criterion | Evidence | Result |
|---|---|---|
| Reference set: no missed pair among exact hits, at most 2 % missed, at most 20 % unfounded from level 3 | `ReferenceSetTests.The_rules_meet_the_thresholds_of_the_reference_set` (table above) | Met; labels to be reviewed by the owner |
| The worked examples of 7.6 give the stated levels | `RuleSetTests.The_worked_examples_give_the_stated_levels`, `RuleSetTests.A_variant_is_level_1_without_computation` | Met |
| The same stored input gives the same level and factors again | `RuleSetTests.The_same_stored_input_gives_the_same_level_and_factors`, `FindingLevelTests.A_new_signal_raises_the_level_and_the_same_input_changes_nothing`, `RecomputeCommandTests` | Met |
| The spike data (one week, three projects) give at most 8 open actions | `ReferenceSetTests.The_spike_week_gives_at_most_eight_open_actions_for_the_three_example_projects`: 6 (PHP level 5; .NET, MySQL, Linux kernel, PostgreSQL level 4; Umbraco level 3) | Met |
| A new project with 40 hits in the lookback gives findings marked as first fill, no `action.raised` and exactly one `backfill.completed` | `ActionTests.A_new_project_with_40_hits_gets_backfill_findings_and_exactly_one_summary` | Met |
| Raising a version in the profile, by hand or by a new commit, closes the findings it no longer concerns | `CandidateSearchTests.The_reverse_search_finds_entries_in_the_lookback_and_a_raised_version_closes_the_finding` (closed as `fixed_by_update`), `IndexRepositoryTests` (a commit changes the component's version) | Met |
| A finding of a component marked «development only» gets at most level 2 unless a floor applies | `RuleSetTests.Development_components_stay_at_2_unless_a_floor_applies`, `FindingLevelTests.A_finding_stores_level_and_factors_and_development_components_stay_low` | Met |
| The three repositories of phase 2 give a number of open actions that is recorded; if it is far above the spike's, the owner decides on bundling transitive packages (16.8) | Observed above | Recorded: 34, far above the spike; **open**: the owner decides on bundling |
| With the sample data, all three answers sit above the action bar at 390 × 844 (automatic layout test) | `web/e2e/flows.spec.ts` «the three answers sit above the action bar» | Met |
| Without a language model, overview, action and alert show all three answers from structured data | `AnswerTests`, `ActionApiTests.Members_read_actions_and_alerts_and_change_their_status` (action, overview, alert), `web/e2e/flows.spec.ts` (overview, action, alert pages) | Met |
| Automatic accessibility check of the pages finds nothing | axe (WCAG 2.2 AA) on every page of `web/e2e/flows.spec.ts` | Met |

## Open points for the owner

- **Bundling transitive packages (master plan 16.8).** The three repositories give 34 open actions against
  the spike's 6 to 8, most of them transitive or development-only npm packages, several the same package in
  different versions. Options: one action per package name across its versions, or one action per direct
  dependency that pulls the vulnerable packages in. Ardea keeps one action per component until decided.
- **Review the reference set's labels.** Master plan 7.9 asks for labels made by a person. The 250 labels
  were made by a language model from the records, without seeing Ardea's results (`by: "model: …"`,
  `reviewed: false`). The labeller marked as ambiguous: P003/P005, P011/P021/P022 and the openejb, Batik and
  FOP pairs of `above_fix_text` (installed versions that probably do not exist), P047, P060, P067, P080,
  P112 (Symfony 1.0.0 looks like a placeholder), P134, P186, P206, P222. Set `reviewed: true` where a label
  holds, correct it where not; the build then measures against the corrected labels.
- **Review the phase 3 mockups** (task 3.1, E15) against the built screens. Deviations are listed in PR #63:
  the overview leaves out «This week's CVE records» and «Open actions, 30 days» (no endpoint gives these
  numbers yet), the actions list has no «Unassigned» chip, the alert page has no history card.
