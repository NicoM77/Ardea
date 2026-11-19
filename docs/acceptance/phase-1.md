# Phase 1 acceptance: from source to consolidated entry

Master plan chapter 14, phase 1. Every criterion is either checked by a fast test in the pipeline or, where
that is not possible, observed once on a real instance (chapter 14 introduction). Observed on 19.11.2026
with the image built from `main` at that time.

## Observed run

A fresh instance from `deploy/compose.yaml` with the reference machine's limits added: all containers pinned
to two CPU cores (`cpuset: "0,1"`), memory limits of 1.5 GB (database), 2 GB (worker), 0.5 GB (API) and
0.5 GB (migrate). Default settings: 180 days look-back, all sources enabled.

| What | Result |
|---|---|
| Start (`docker compose up`) | 06:31:54 UTC |
| First load drained (no job left) | 07:22:51 UTC: **51 minutes** |
| CVE Program, backfill from the full archive | 94 170 records in 7 min 42 s, **0 quarantined** |
| OSV, six ecosystems from their archives | 23 804 records in 1 min 20 s |
| CISA KEV | 1 786 listings |
| FIRST EPSS | 408 395 scores in 1 min 11 s |
| Correlations | 132 907 runs → 102 162 published, 1 638 rejected, 114 disputed entries |
| Database size | 1.9 GB (master plan 4.10 estimate: 1 to 2 GB) |
| Memory at rest | database 690 MB, worker 227 MB, API 100 MB |
| NCSC Cyber Security Hub, BACS warnings | **failed**: both answered without a content type from this environment (see open points) |

The first 14 minutes ran without the indexes of #40: correlation managed 330 entries a minute and was bound
by the database (scans of all 408 395 signals per entry, and job claims walking the backlog of another
kind). With the indexes, created live at 06:47, it ran at 3 232 a minute; with them from the start, the load
would end after about 43 minutes. Without #40 it would have taken about six and a half hours. `QueryPlanTests`
now pin both index uses.

## Criteria

| Criterion | Evidence | Result |
|---|---|---|
| First load with 180 days look-back runs without manual work; duration on the reference machine observed and documented, target under 60 minutes | Observed run above | **Met**: 51 min (≈ 43 min with #40 from the start) |
| A new or changed CVE record is in the system at most 90 minutes after publication | By design: CVE Program interval 60 min with conditional requests (`Unchanged_log_costs_one_conditional_request`), normalisation and correlation queued at once; the observed correlation rate keeps a day's ~3 400 changes far below that | Met by design; not separately timed |
| The same delta processed twice changes nothing: no new revision, no follow-up job | `SyncSourceTests.The_same_delta_twice_creates_no_revision_even_when_reformatted`, `CorrelationTests.The_same_delta_twice_changes_nothing_no_revision_and_no_follow_up_job` | Met |
| Against a recorded change log with gaps and duplicates no id is missing after the check | `MaintenanceTests.A_change_log_with_gaps_and_duplicates_leaves_no_record_missing_after_the_check` | Met |
| A CVE in CVE Program, KEV and EPSS is one entry, every field names its source; an NCSC advisory naming it is a further source and sets the national mark, also when the CVE is added to the advisory later | `Phase1AcceptanceTests.One_cve_from_every_source_is_one_entry_with_provenance_and_the_national_mark`, `ProjectionTests.Issuer_text_and_cna_severity_win_and_the_national_advisory_sets_the_mark` | Met |
| Quarantine share for CVE Program under 0.5 % | Observed run: 0 of 94 170 | Met |
| A simulated outage of 48 hours is caught up without manual work and without a gap; pauses over 30 days (full archive) have their own test with a reduced archive | `CveProgramProviderTests.Incremental_run_fetches_each_change_after_the_cursor_once_and_checkpoints_per_log_entry`, `…Pause_longer_than_the_log_falls_back_to_the_archive`, `SyncSourceTests.Records_and_cursor_commit_at_a_checkpoint_and_a_failed_run_resumes_there` | Met |
| The daily EPSS run touches only entries whose class changes | `SignalTests.Daily_epss_run_touches_only_entries_whose_class_changes` | Met |
| An OSV record that first comes with a GHSA id only, while the CVE record already made an entry, gives exactly one entry once the CVE id arrives; the other key redirects | `OsvMergeTests.A_ghsa_record_that_later_names_its_cve_merges_into_the_cve_entry_and_its_key_redirects` (recorded real records) | Met |
| For the six ecosystems the version check gives the expected result on ten recorded records each | `OsvVersionCheckTests` (60 cases) | Met |

## Open points

- **NCSC Cyber Security Hub and BACS warnings could not be fetched from this environment.** Both answered
  without a content type instead of JSON, and the adapters reacted as designed: a failed run, back-off and
  the source flagged. Whether the endpoints block this network or changed their interface remains to be
  checked from the owner's network. Their behaviour is covered by tests with recorded responses.
- **Found during the run and fixed:** `init-env` failed on a folder of the host user (#37); correlation
  indexes (#40).
