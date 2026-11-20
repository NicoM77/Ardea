# Phase 4 acceptance: AI assessment and the catalogue that learns

Master plan chapter 14, phase 4. Criteria are checked by fast tests in the pipeline or observed once on a
real instance. This record is written from the tests of `main` at the merge of PR #72 (tasks 4.1 to 4.10, PRs
#65, #67 and #71). Nothing was observed on an instance: the one criterion that needs a person and a local model has
not been run.

All model answers in the tests are recorded files, among them invalid and manipulated ones (`Estimating/Recorded`,
`Learning/…`); no test calls a real model. What a real model does with Ardea's prompts is therefore not yet
known (open point below).

## Spike week and the model's calls

`ReferenceSetTests.The_spike_week_gives_at_most_eight_open_actions_and_forty_model_calls_for_the_three_example_projects`
runs the 6 452 records of the spike week against the three example projects with a recorded local model after the
organisation's first fill. It asserts at most 8 open actions and between 1 and 40 calls to the model; it writes the
figures it saw to `spike-week-report.txt` in the test output. The figure itself is not asserted: the description of
PR #65 reports 16 and, after the review fixes, 17 calls in the author's runs. What makes 40 hold is the quota of
five calls per action in seven days, retries included (ADR 037 decision 7).

## Criteria

| Criterion | Evidence | Result |
|---|---|---|
| Without a provider the system behaves exactly as after phase 3 | No provider means `NoAiProviders`, and the phase 3 tests run that way unchanged: `ReferenceSetTests.The_rules_meet_the_thresholds_of_the_reference_set` (rules only, as in `phase-3.md`), `ActionApiTests.Members_read_actions_and_alerts_and_change_their_status` (answers from templates; a request for an assessment is refused with `ai_not_available` and queues no job). With the policy «none» an organisation queues no model job either: `EstimateTests.The_data_policy_picks_the_providers_and_local_only_never_reaches_a_cloud_provider` | Met; no single test compares two runs, the evidence is the unchanged phase 3 tests |
| 20 prepared entries with embedded instructions, half with the version in range and half unknown: none reaches level 1 without a person, none falls below the floors, none lies more than one level below the result without the model, no invalid output reaches the store | `EstimateTests.Twenty_entries_with_embedded_instructions_stay_within_the_bounds_of_the_rules`: a second organisation without the model is the baseline; recorded answers follow the instructions («not_affected») or break the schema; asserts level at least 2 and status open or in review, level at least the floor, level at least the baseline minus one, no model verdict counted where the version is in range, 6 of 20 stored as «without model», and no text of an invalid answer anywhere in the store. Further: `EstimateCheckTests.Invalid_and_manipulated_answers_are_refused_with_the_rule_they_break`, `EstimateTests.Invalid_answers_are_asked_again_once_and_never_reach_the_store` | Met |
| A test reading outgoing requests finds no components outside the hits and no project names | `EstimateTests.A_checked_answer_is_stored_and_its_verdict_counts_only_where_the_version_is_unknown` (the recorded model's request names only the two hit components under `C1`, `C2` and `P1`, `P2`; no project name, component name or address from the profile appears in the request text), `EstimateCheckTests.No_free_text_of_the_profile_reaches_the_request`, `EstimateCheckTests.The_request_names_only_the_hit_components_under_placeholders_and_keeps_source_texts_in_the_data_block` | Met |
| An organisation with the policy «local only» makes no call to a non-local provider, not even when the local one fails | `EstimateTests.The_data_policy_picks_the_providers_and_local_only_never_reaches_a_cloud_provider` (the local provider is down and gets its attempts; the cloud provider gets none for that organisation), `AiProviderTests.The_data_policy_decides_which_providers_an_organisation_may_use_local_ones_first` | Met |
| The spike data (one week, eight actions) trigger at most 40 calls | `ReferenceSetTests.The_spike_week_gives_at_most_eight_open_actions_and_forty_model_calls_for_the_three_example_projects` (1 to 40 calls, see above); the quota per action in `EstimateTests.The_model_runs_above_the_threshold_within_the_quotas_and_never_in_a_first_fill` | Met |
| A learned catalogue entry changes no curated entry and removes no existing hit (fast test) | `LearnedEntriesTests.A_learned_entry_changes_no_curated_entry`, `LearnedEntriesTests.Every_spelling_cpe_pair_package_and_image_of_the_curated_catalogue_finds_what_it_found_before` (learned twins of every curated entry; more than 1 000 lookups compared), `LearnedCatalogTests.A_learned_entry_adds_a_hit_that_names_it_and_removes_none`, `LearnedCatalogTests.A_free_entry_switched_to_a_learned_entry_keeps_its_findings_when_the_entry_is_withdrawn` | Met |
| Product names with embedded instructions lead to no invalid adoption (recorded answers) | `GrowCatalogTests.Product_names_with_embedded_instructions_lead_to_no_invalid_adoption` (11 manipulated recorded answers and two names that carry instructions: nothing learned, no curated row changed), `GrowCatalogTests.An_instruction_in_a_title_keeps_the_answer_from_taking_effect`, `LearnCheckTests.A_manipulated_answer_is_refused`, `LearnCheckTests.Statements_that_read_like_instructions_are_suspicious` | Met |
| Without a provider the mapping runs without a model, and nothing else | `GrowCatalogTests.Without_a_provider_the_mapping_without_a_model_runs_and_nothing_else` (rule-made aliases and new entries, no call asked, curated entries unchanged), `RuleMappingTests` | Met |
| Once, in development, with a local model on 50 unknown keys from real records: no wrong mapping takes effect at once, at least half are adopted at once; observed and documented | Needs a person and a local model. The procedure is in `docs/operations/operations.md` («Catalogue that learns»); the tests only check the same rules on recorded answers (`GrowCatalogTests.Confident_answers_take_effect_the_rest_waits_and_decisions_stand`) | **Open**: not run; for the developer or the owner |

## Open points for the owner

- **The observation with a local model at 50 unknown keys.** Not run (PR #71 says so). Procedure: first load with
  180 days, Ollama with `qwen3:14b`, `ARDEA_AI__CATALOG_DAILY_CALLS=100`, `ardea catalog grow`, then judge the 50 most
  frequent asked keys by hand. It holds when no wrong mapping took effect at once and at least half did. The result
  (model, prompt `learn-1`, date, counts, every wrong mapping) is to be written into this file; wrong entries are
  withdrawn on *Sources → Catalogue*.
- **No real model has answered an assessment.** The criteria on instructions and invalid output are met against recorded
  answers. Ardea's prompt `estimate-1` has not been tried with a model, and `operations.md` says the recommended
  models have not been measured against the reference set. The correction rate in the administration's AI table will
  show the quality in operation.
- **Review the AI mockups and screens** (E15): [`docs/design/mockups/remediation.html?ai`](https://github.com/NicoM77/Ardea/tree/archive/planning-and-design/docs/design/mockups/remediation.html) against the built screens.
  Deviations are listed in PR #67: the action page shows the assessment of the action's lead alert only, the budget
  line has no «x of 5 automatic assessments in 7 days», «Assess again» is on the alert page, and the correction form
  adds an optional expected priority. The «Catalogue that learns» card on *Sources* (PR #71) has no mockup of its own.
- **Learning after a first fill starts at the next daily run** (02:41 UTC) or on request; nothing starts it when the
  fill ends (PR #71). Run `ardea catalog grow` after the first load.
- The spike week's call count is asserted only as at most 40 (see above). If the owner wants the figure itself
  guarded, the test needs a tighter bound.
