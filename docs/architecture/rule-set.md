# Rule set and levels

How a vulnerability becomes something an organisation sees: a finding with a priority, a reminder, or a notice. The
assessment is a funnel of cheap, deterministic steps. The level comes from a rule set; a language model, when one is
configured, sees only a small rest and can move the level a little, never set it.

Three properties hold throughout:

- **Complete without a language model.** Matching, version check and rule set give the level, the reasoning and
  the reminder from structured data and templates.
- **Reproducible.** Every level can be computed again from what is stored: the entry, the profile, the rule set
  version and, where there is one, the model's stored verdict. The same input gives the same level and factors.
- **Traceable.** Every factor names its evidence, and what is unknown says so.

The rule set is `data/rules/ruleset-1.json`, checked when it is loaded; the engine is
`src/Modules/Assessment/Ardea.Assessment/Rules/RuleEngine.cs` ([ADR 034](../adr/0034-rule-set-file.md)).

## Candidate search

A changed entry is searched against every profile (forward); a changed profile is searched against the entries of the
last 180 days (reverse). The keys of the affected products (`intel.entry_affected`) are compared with the keys of the
components (`assets.component_key`); free text never counts
([ADR 033](../adr/0033-candidate-search-and-version-verdicts.md)).

| Match type | Condition | Strength |
|---|---|---|
| `package` | ecosystem and package name agree | strong |
| `cpe` | vendor and product of the CPE agree | strong |
| `product` | the catalogue's product key agrees, directly or through an alias | strong |
| `variant` | the catalogue lists the component's product as related but separate (MariaDB and MySQL) | weak |
| `ecosystem` | the entry concerns a plugin or package of a platform in the profile that the profile does not list | weak |
| `name` | a free entry without a vendor matches only by name | weak |

A weak match is level 1 with its reason and no notification. The number of weak matches of a project is a hint at
gaps in its profile.

A strong match then takes one of two ways, by the component's version precision
([ADR 046](../adr/0046-stack-notices-and-reminders-without-deadline.md)):

- **Precise** (`exact` version): a finding with a level, which joins a reminder from level 2 on.
- **Coarse** (no version, or only a line such as «PostgreSQL 17»): a [notice](#notices), «might affect you». The
  organisation's stack is coarse by nature; a component that gets an exact version changes ways by itself.

## Version check

The installed version is compared with the entry's ranges in the scheme of the ecosystem or the record: SemVer (also
npm, Go and Packagist), NuGet, PEP 440, Maven, RPM, and a tolerant numeric scheme as fallback. Git commits are
stored but not compared. The result has three values:

| Result | Meaning |
|---|---|
| in range | the version is in an affected range, or in no range while the record says every other version is affected |
| out of range | the version is in an unaffected range, the record says every other version is unaffected, or it is above every affected range and nothing in the record names a later fix |
| unknown | no version, a version the scheme cannot read, a distribution package (distributions backport fixes without changing the upstream version), or anything else |

*Unknown* never means *not affected*: the finding stays and says what is unknown. The full reading of the records'
statements, including how text that names a later fix overrides the structured ranges, is in
[ADR 033](../adr/0033-candidate-search-and-version-verdicts.md).

## Levels

Five levels, shown in the interface as a priority word with its own symbol and no digit. The number orders them
internally.

| Level | Priority | Typical cases | Reminder rhythm (default) |
|---|---|---|---|
| 5 | Critical | actively exploited, version in range, high or critical severity, system reachable | daily |
| 4 | High | critical severity in range; high severity in range on an exposed or business-critical system; any active exploitation in range | weekly |
| 3 | Medium | high severity in range on an internal system of normal criticality; medium severity in range on an exposed one; critical severity with an unknown version | monthly |
| 2 | Low | every other strong match | in digests only |
| 1 | Not relevant | checked, does not affect the organisation: no open finding, no reminder | none |

## Points

Rule set version 1 adds whole points. The interface shows each factor as a line with its evidence and effect.

| Factor | Values | Points |
|---|---|---:|
| Severity (CVSS base score: critical from 9.0, high from 7.0, medium from 4.0, low below) | critical · high · medium · low · no score | 4 · 3 · 2 · 1 · 2 |
| Exploitation (KEV, SSVC and the sources' statements) | active · public exploit · none known | 3 · 2 · 0 |
| Affectedness | version in range, or with an unknown version a person's or the model's «affected» or «likely» · unknown · «unlikely» | 1 · 0 · −2 |
| Criticality of the system | high · normal · low | 1 · 0 · −1 |
| Exposure | internet · internal · isolated | 1 · 0 · −1 |
| Protection | effective · none stated | −1 · 0 |
| Warning of a national authority about this entry (a source operated by an authority or on a state mandate) | yes · no | 1 · 0 |
| EPSS | 50 % or more · below | 1 · 0 |

| Sum | Level |
|---|---|
| 8 or more | 5 |
| 5 to 7 | 4 |
| 4 | 3 |
| 3 or less | 2 |

Criticality, exposure and protection come from the component, or from its project where the component states none.
The EPSS class changes only past a margin (above 55 % to high, below 45 % to low), so a score around 50 % does not
flip a level back and forth.

Example: critical severity (4), actively exploited (3), version in range (1), a business-critical system (1), internal
(0): 9 points, level 5.

## Order of the rules

For every finding, in this order:

1. **Exclusion, level 1 without points:** the match is weak; the entry is withdrawn or merged; a person set the
   finding to «not affected»; or the version check says *out of range*. An exclusion rests on structured data or on a
   person, never on the model, and comes before everything else, the floors included.
2. **Points** as above, and the level from the sum.
3. **Caps:**
   - 4 as long as neither the version check nor a person confirmed the component is affected: Critical is kept for
     what is confirmed.
   - 2 for components used only in development (scope `development` from the repository), unless a person rates
     them otherwise. The floors below still apply to them.
4. **Floors**, which no other factor goes below:
   - 3 when the vulnerability is actively exploited and no exclusion applies (the version is in range or unknown);
   - 4 when it is actively exploited and the version is in range;
   - 5 when in addition the severity is high or critical and the system is not isolated;
   - at most one level below the result without the model, as long as no person confirmed the model's verdict.

The level belongs to the finding, that is to one component. A reminder's level is the highest of its open findings,
an alert's the highest of its findings. The stored explanation (`finding.factors`) holds the rule set version, the
exclusion or the eight factors with value, evidence and points, the sum, and every cap and floor that changed the
level.

## Language model

Optional ([ADR 037](../adr/0037-assessment-by-language-model.md)). It answers what rules cannot: under which
conditions the vulnerability can be exploited, whether they plausibly hold here, and what to do first. One call
covers an alert, that is one entry for one organisation, with every component it hits.

- **When.** After the rules, as a job of its own, when a finding of the alert reaches level 4 or 5 by the rules, or
  level 3 with an unknown version, and every reminder concerned has had fewer than five automatic calls in seven
  days. Never during a first fill. Daily token caps per instance and per organisation apply; a person can ask for an
  assessment in the interface within a daily limit ([Operation](../operations/operations.md#language-model)).
- **Where.** Only at providers the organisation's data policy allows: no model, local only (the default), or cloud
  allowed. «Local only» never falls back to a cloud provider.
- **Input.** The entry's public data and, of the profile, only fixed values of the hit components (key shared with
  the entry, kind, version, version check, packaging, exposure, criticality, protection, scope). No names of
  components, projects or the organisation, and no free text.
- **Effect.** The verdict per component counts only through the affectedness factor, and only where the version
  check could not decide. «Not affected» from the model counts like «unlikely» and is shown as a proposal a person
  confirms. The model lowers a level by at most one and cannot exclude. A person can confirm or correct the
  assessment, and the finding is assessed again ([ADR 039](../adr/0039-review-of-an-assessment.md)).
- **Failure.** An answer that does not fit the schema or names components or sources it was not given is refused
  and asked once more; after two failures the rules alone apply.

Without a model the three answers (what happened, are we affected, what to do) come from templates: the entry's
title and first sentences, the match with the installed and affected versions, and the reminder's target version or
the source's solution and workaround texts.

## Reminders

A reminder (table `assess.remediation`, an *action* in the API) gathers every open finding of one component in one
project: «PostgreSQL in project C: update to 17.12» may stand for many vulnerabilities. It is the unit for work and
notifications.

| Property | Rule |
|---|---|
| Level | the highest of its open findings |
| Target | each finding knows the fix of the range the installed version is in; the target is the highest of them. Without one: the source's solution and workaround texts, else «no fix stated». |
| Status | open or done; no assignee, no deadline. The result (remediated, risk accepted, not affected) is set per finding, or for all at once; the reminder is done when no finding is open. |
| Growth | a new finding from level 2 joins the open reminder of its component; people hear about it only when the level or the exploitation rises |
| Closing by itself | raising the version in the profile closes every finding whose range no longer contains it («fixed by update»); a removed component closes its findings too |
| Reopening | a finding a person closed opens again only when its level rises or the version check says *in range* again |

Open reminders remind by level: by default level 5 daily, 4 weekly, 3 monthly, lower ones only in the digests. Each
organisation sets the rhythm per level (daily, weekly, monthly or off). Reminders are sent in one run per
organisation on weekdays at 08:00 in its time zone, one message per recipient and channel listing every reminder
due ([ADR 048](../adr/0048-notice-raised-and-reminders-by-level.md)).

## Notices

A notice is raised for an entry that hits a coarse component, once per organisation and entry, naming the components
that raised it ([ADR 047](../adr/0047-notice-generation-and-answers.md)). A version line narrows first: an entry whose
ranges rule the line out raises nothing. Each coarse component has a threshold:

- **Everything:** every strong match raises a notice.
- **Only important** (the default): a CVSS score of 9.0 or more, active exploitation, a national authority's
  warning or a high EPSS class. For products the catalogue marks as noisy (the large browsers), only active
  exploitation or an authority's warning ([ADR 044](../adr/0044-notices-per-week-and-thresholds.md)).

A notice has no level; the Notices page puts exploited ones first, then sorts by CVSS score. People answer it:

| Answer | Result |
|---|---|
| Affects us | a normal reminder, optionally with project and version; with a version the component is checked precisely from then on |
| Doesn't affect us | done; it returns once only if the vulnerability later becomes actively exploited or listed in KEV |

Either answer can be taken back. For the notification rules a notice that is actively exploited or in KEV counts as
meeting every level threshold, any other as level 2 ([ADR 048](../adr/0048-notice-raised-and-reminders-by-level.md)).

## When levels are recomputed

| Trigger | Scope |
|---|---|
| An entry changes materially | every organisation with a finding or a new match for it |
| A profile changes, by hand or by reading a repository | the project against the entries of the look-back and its open findings |
| A person confirms, rejects or corrects an assessment | that finding |
| A new rule set version | every alert, with `ardea assess recompute` |
| The catalogue learns or withdraws an entry | the entries whose product keys it changes, then their alerts ([ADR 040](../adr/0040-catalogue-that-learns.md)) |

**First fill.** A new project or a newly linked repository does not bring hundreds of alerts: the reverse search
marks its findings and notices as first fill, raises no single events and gives one summary per project
([ADR 035](../adr/0035-actions-history-and-first-fill.md)).

## Measuring the rules

A reference set of labelled pairs of entry and component (`data/reference/`, labelling rules in
`data/reference/LABELLING.md`) runs through the real pipeline in the build, with rules only
([ADR 036](../adr/0036-reference-set-and-calibration.md)). *Missed* is a pair labelled affected that gets level 1;
*unfounded* is a pair labelled not affected with level 3 or higher. The build fails with any missed pair among the
exact matches, more than 2 % missed overall, or more than 20 % unfounded. A change to the rule set or the catalogue
is a change to this run. The quality of a language model shows in operation, as the rate of corrections per model
and prompt version in the administration.
