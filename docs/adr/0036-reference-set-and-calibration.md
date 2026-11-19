# ADR 036: Reference set, spike week and calibration

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 007 (level from a versioned rule set), ADR 033 (version verdicts), ADR 034 (rule set file)
- Master plan: 6.6, 7.6, 7.9, 14 (phase 3 acceptance), task 3.7

## Context

Master plan 7.9 asks for at least 200 pairs of entry and project profile, selected from real records by fixed
rules (a third each: exact product and version hits, hits with an unknown or unusable version, non-hits with
similar names; at least 30 above every named range, half of them affected), labelled by a person from the
source, and checked in the build with rules only in under a minute: no missed pair among the exact hits, at
most 2 % missed, at most 20 % unfounded. Phase 3 also accepts at most 8 open actions for the spike week
against the three example projects. Open were where the data lives, how the pairs are chosen and run, who
labels them, and what calibration changes.

## Decision

1. **Data in `data/reference`.** `records.jsonl` holds the records of the pairs as the CVE Program published
   them (the spike week, 11 to 17 November 2026), `pairs.json` the pairs with project, one component, the
   reason for their selection and the label, `LABELLING.md` the labelling rule, `spike-week.jsonl.gz` the
   week as a test dataset. Records are kept whole: the pipeline reads them as the source does.
2. **Selection by script.** `tools/reference-set/select_pairs.py` picks pairs by fixed, documented rules with a
   seeded order, and caps pairs per product. It never labels. Pairs keep their ids and labels when it runs
   again; new pairs get new ids. The first run gave 230 pairs: 90 exact (44 in a range, 46 above every range),
   70 with an unknown or unusable version (no version, distribution package, Git commits only), 70 similar
   names (another product containing the name, a shared distinctive word, a WordPress plugin against
   WordPress). Labelled, only 6 of the 46 above every range were affected: most records name the fix at
   the range bound. A second rule added the week's 20 records whose prose names a fix above the installed
   version (`above_fix_text`).
3. **The evaluation runs the real pipeline.** `ReferenceSetTests` stores the records as the CVE Program source
   would, runs every queued job through the job registry (normalise, correlate, match, assess, actions) with
   one organisation per pair, and compares each pair's highest finding level with its label. It writes
   `reference-report.md` (measures, level matrix, groups, every disagreement) and fails below the thresholds,
   with fewer than 200 labelled pairs, or with fewer than 30 labelled pairs above every range. It runs in
   about 30 seconds.
4. **The spike week** runs the same way against projects A, B and C of the requirements, with the versions
   installed before that week's security updates, and checks at most 8 open actions. The dataset keeps the
   1 812 of 6 452 records that mention one of the projects' technologies anywhere; the full week gave the
   same actions once (five before calibration), and the filtered one runs in under a minute.
5. **Labels by a model, reviewed by the owner.** The plan wants a person to label. The labels were made by a
   language model from the records alone, without seeing Ardea's results or code, following
   `LABELLING.md`; each says so (`by: "model: …"`, `reviewed: false`), and the report counts labels a person
   made or reviewed. They count for the build. Phase 3 is accepted only after the owner has reviewed them,
   in the same way as the mockups (E15).
6. **Calibration changed the version check, not the weights.** The first run missed 13 affected pairs and
   had 1 unfounded. None was a matter of points: they were range normalisation (wildcards, one-field ranges),
   Google's lower bound equal to the fix, version numbers typed as Git, unreadable lower bounds, scheme
   choice, a vendor form in the catalogue, and fixes named only in the prose (ADR 033 decision 11). After
   these fixes, with all 250 pairs: 0 of 67 affected pairs missed, 0 of 117 not affected pairs unfounded,
   239 levels as labelled and 249 within one; 66 pairs above every range, 22 of them affected.
   Rule set version 1 keeps its weights; the remaining level differences are cases where «unknown» scores
   below the labeller's «affected», which is the plan's intent.

## Consequences

- Every change to the catalogue, the parser, the version check or the rule set runs against the reference
  set in the pipeline. A change that misses an affected pair fails the build.
- The set is one week of records, 250 pairs. Its selection rules are in code and can draw further weeks.
- Owner review of the labels is an open point of phase 3 (README).
- Stored entries keep their old rows until `ardea admin intel-recompute` and `assess-recompute` run.
