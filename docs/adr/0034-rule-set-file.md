# ADR 034: Rule set version 1 as a file

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 007 (level from a versioned rule set with bounds)
- Master plan: 4.9, 7.6, task 3.4

## Context

Master plan 7.6 fixes the factors, points, level thresholds, bounds and default deadlines of rule set
version 1, and 4.9 says the rule set is a file in the image whose version is stored with every finding.
Open were the file's shape and place, how CVSS scores become severity classes, and how the explanation is
stored so that the same input gives the same level and factors again (phase 3 acceptance).

## Decision

1. **One JSON file per version**, `data/rules/ruleset-{version}.json`, shipped with the image like the
   catalogue (`COPY data/`, `ARDEA_DATA_DIR`). The highest version in the folder is in effect. The file holds
   points per factor and value, CVSS thresholds per severity class, level steps, caps, floors and default
   deadlines in hours. It is read once per process and checked on load: every factor and value of version 1
   present and nothing unknown, levels falling, every level from 2 to 5 with a deadline. A broken file stops
   the assessment with the list of its problems instead of computing other levels.
2. **The engine is code, the numbers are data.** The order of 7.6 (exclusion, points, caps, floors) and which
   cap or floor exists are fixed in `RuleEngine`; the file changes their values. A rule set that needs a new
   kind of factor or bound is a new version of the code as well.
3. **Severity classes** after CVSS: critical from 9.0, high from 7.0, medium from 4.0, low below; a score of
   0.0 counts as low, no score as `unrated` (2 points, as 7.6 says).
4. **Exclusions** come first, in this order: weak match, withdrawn or merged entry, a person's «not
   affected», version out of range. They give level 1 without points and without factors.
5. **The explanation is stored with the finding** (`finding.factors`): rule set version, exclusion, the eight
   factors in fixed order (each with value, evidence and points, zero points included, so the interface can
   show one line per factor), the sum, the level from the sum, and each cap and floor that changed it.
   Evidence is derived from stored inputs only (CVSS version and score, the exploitation source and date,
   the installed version and the version check), so recomputing the same input gives the same JSON.
6. **The model's floor** compares against the level the same rules give without the model's verdict, after
   the same caps and floors. It takes effect from phase 4; the input is there already.

## Consequences

- Calibrating the weights against the reference set (task 3.7) changes the file, not the code, and is a new
  version. Recomputing open findings after a new version, with the reason «rule set updated», comes with the
  command of task 3.11.
- Deadlines per organisation (master plan 7.6: adjustable per organisation in the MVP) are not stored yet;
  the actions of task 3.5 take the file's defaults.
