# Labelling rule for the reference set

The reference set (master plan 7.9, task 3.7) pairs a real CVE record with a small project profile and
states what a careful person expects Ardea to say about it. The build checks the rules against these
labels (`ReferenceSetTests`). A label is only worth something if it is made from the record, not from
what Ardea computes: **label without looking at Ardea's result for the pair.**

## Files

- `records.jsonl`: the records, one CVE JSON 5 record per line, exactly as the CVE Program published them
  in the week of 11 to 17 November 2026 (the spike week, `prototypes/relevance-funnel`). CVE® records
  © The MITRE Corporation, used under the CVE Program Terms of Use.
- `pairs.json`: the pairs. Each names a `record`, a `project` (criticality, exposure) and one `component`
  (catalogue product key or package, installed version, packaging). `group`, `subgroup` and `selection`
  say why the pair was selected; they are no hint at the answer.
- `tools/reference-set/select_pairs.py` selects the pairs by fixed rules and keeps existing labels.

## What to decide

For each pair, read the whole record: every `affected` entry, the descriptions, the problem types, the
CVSS metrics of the publisher (CNA) and of CISA (ADP), and the reference names. Then fill `label`:

```json
"label": {
  "verdict": "affected | not_affected | undecidable",
  "level": 1,
  "reason": "One to three sentences with the facts of the record that decide it.",
  "by": "who labelled",
  "reviewed": false
}
```

### Verdict

1. **Same product?** The component is affected only when the record means this product. A plugin or theme
   of a platform is not the platform; a driver, SDK, client or cloud service is not the server product
   whose name it contains; a product of another vendor with a similar name is another product. If the
   record does not mean the product: `not_affected`.
2. **Version.** When it does mean the product:
   - `affected` when the installed version is affected according to the record. Structured ranges and
     prose are read together. Where they disagree, the more specific statement wins: a description saying
     "fixed in 2.5" makes 2.4 affected even when the ranges stop at 2.3; a description saying "2.3 and
     earlier" with no later version mentioned leaves 2.4 unaffected.
   - `not_affected` when the installed version is outside what the record states as affected, read the same way.
   - `undecidable` when the record does not allow a decision: the profile has no version; the component is
     a distribution package (distributions backport fixes, so the upstream version does not tell);
     the record states versions only as Git commits or in a form that cannot be compared with the
     installed version, and the prose names no version either.
3. Note any conflict between ranges and prose in `reason`. That is the case the pairs with an installed
   version above every range (`aboveRanges: true`) are there for (master plan 6.6, fifth row).

### Level

The level is what rule set version 1 gives for your verdict (master plan 7.6, `data/rules/ruleset-1.json`):

- `not_affected`: level 1.
- Otherwise add the points:
  - **severity** from the publisher's CVSS base score, the newest CVSS version it gives; without one, CISA's; without any, `unrated`. Critical from 9.0, high from 7.0, medium from 4.0, low below.
  - **exploitation** from CISA's SSVC `Exploitation` option (`active`, `poc`, `none`); a CISA KEV entry in the record counts as `active`; nothing stated counts as `none`.
  - **affectedness** `confirmed` for `affected`, `unknown` for `undecidable`.
  - **criticality** and **exposure** from the pair's project.
  - **protection** `none`, **authority warning** `no`, **EPSS** `low` (the record carries none of them).
- Map the sum to the level with the rule set's steps, then apply its bounds:
  - an `undecidable` verdict is capped at the `unconfirmed` level;
  - the floors for actively exploited entries apply.

Levels show how the rules and the labeller differ and drive the calibration. The build's pass or fail
depends on the verdict, with two measures:
- **missed**: a pair labelled affected that gets level 1;
- **unfounded**: a pair labelled not affected that gets level 3 or higher.

## Who labels

Master plan 7.9 asks for a person to label each pair from the source. `by` names who did: a person's
name, or `model: <name>` for a label a language model made. `reviewed: true` records that a person
checked a label someone else made. The evaluation reports how many
labels a person has made or reviewed. Labels made by a model count for the build, but phase 3 is
accepted only once the owner has reviewed them (README, open points).
