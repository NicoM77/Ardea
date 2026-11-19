# Phase 2 acceptance: organisations, projects and profile

Master plan chapter 14, phase 2. Criteria are checked by fast tests in the pipeline or observed once on a
real instance. Observed on 19.11.2026 on the acceptance instance of phase 1 (2 vCPU, 4 GB) after an update
to the image built from `main` (with tasks 2.10 to 2.13).

## Observed: three repositories read (master plan 16.8)

Three public repositories, linked without a token to one project; the schedule picked them up within its
five-minute cycle and read all three in 20 seconds, each with the SBOM of the dependency graph and the lock
and platform files.

| Repository | Files read | Components | Version unknown | Development | Direct / transitive |
|---|---|---|---|---|---|
| actions/checkout | 4 | 565 | 5 (0.9 %) | 533 | 19 / 538 |
| actions/setup-node | 10 | 491 | 5 (1.0 %) | 415 | 31 / 451 |
| expressjs/express | 1 | 53 | 36 (67.9 %) | 0 | unknown |

- Ecosystems: npm 1 083 components, GitHub Actions 26 (shown as not yet checkable, master plan 16.3).
- express has no lock file: the SBOM carries manifest ranges, so two thirds of its packages are «version
  unknown» and scope and relation are unknown, as ADR 032 predicts. The coverage hint «packages without lock
  file» says so.
- Platform suggestions with evidence: Node.js 18 and 24 (from `.nvmrc` and `engines`), Ubuntu (from a
  Dockerfile).
- Ardea's own repository is private and needs the owner's fine-grained token; it is the one reading still to
  do (open point).

## Criteria

| Criterion | Evidence | Result |
|---|---|---|
| The cross-tenant test finds no way to another organisation's data for any endpoint | `TenantIsolationTests.No_organisation_endpoint_reaches_another_organisation` (every `/orgs/{orgId}` endpoint, foreign ids in path and body), row-level security on every organisation and user table | Met |
| A test person who does not know the system enters the three example projects in the wizard in under 10 minutes | Needs a person; the wizard (2.7) and its e2e flow are in place | **Open**: for the owner |
| The catalogue search gives the right product first for 50 predefined inputs including spelling variants | `CatalogSearchTests` (50 inputs) | Met |
| Every change to the profile is in the audit log and queues the reverse search for the project | `ProjectApiTests.A_project_holds_catalogue_and_custom_components_and_every_change_is_audited_and_queues_the_reverse_search`, `IndexRepositoryTests` (readings audited with commit origin) | Met |
| Replacing a profile as a whole with only one version changed keeps every component id | `ProjectApiTests.Replacing_the_profile_with_one_changed_version_keeps_every_component_id` | Met |
| A new commit that only raises one package version changes exactly that component; all ids stay | `IndexRepositoryTests.A_commit_that_only_raises_one_package_version_changes_exactly_that_component_and_keeps_every_id` | Met |
| An empty or more than half smaller SBOM removes nothing but is held back for confirmation; a failed reading does not change the profile | `IndexRepositoryTests.A_much_smaller_state_is_held_back_until_confirmed_and_a_failed_reading_removes_nothing` | Met |
| No process is started and no file outside the limits is read (prepared repository: oversized file, lock file with script entries, XML with external entity) | `IndexRepositoryTests.A_prepared_repository_is_read_as_data_within_its_limits`, `PlatformDetectionTests.Hostile_files_are_read_as_data_only` | Met |
| The test person enters the three example projects in under 10 minutes also when one comes from a repository | Needs a person | **Open**: for the owner |
| Ardea's own repository and two others are read; component count, share «version unknown» and duration are observed once | Observation above, with three public repositories | Met except Ardea's own repository (private, needs a token) |

## Open points

- The timed wizard run with a test person (two criteria).
- Reading Ardea's own repository with a fine-grained token (Contents: read), which also confirms the rights
  of ADR 032 on a private repository.
