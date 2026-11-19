/* Ardea — phase-3 mockups (task 3.1): a remediation with several findings, the same with an AI
   assessment, and an alert from the organization's view. Field names follow master plan 4.6 and 10.2
   (remediation, finding, alert, assessment, assess_event), so the later pages can follow them.
   Real: CVE-2026-116325 is a real identifier from the planning spike. Invented: every other
   identifier and text here, the versions, dates, people and the model's output. */
(function () {
  'use strict';

  /* Rule set version 1 (master plan 7.6). Factor order as the plan lists them. */
  var FACTORS = ['Severity', 'Exploitation', 'Match', 'System criticality', 'Exposure', 'Protective measure', 'National warning', 'EPSS'];
  function level(sum) { return sum >= 8 ? 'critical' : sum >= 5 ? 'high' : sum === 4 ? 'medium' : 'low'; }
  function points(factors) { return factors.reduce(function (n, f) { return n + f[1]; }, 0); }

  /* factors: [evidence, points] in FACTORS order */
  var REMEDIATIONS = {
    postgres: {
      number: 143, title: 'Update PostgreSQL to 17.12', project: 'Platform Ops', component: 'PostgreSQL', installed: '17.9',
      target: '17.12', targetNote: 'Fixes all four findings: the highest fixed release of the 17 branch among them', via: null,
      status: 'open', assignee: null, openedAt: '2026-11-17T06:10', due: '2026-11-24T06:10', exploitation: 'none', fixAvailable: true,
      answers: {
        happened: 'Four flaws in PostgreSQL 17. The worst lets a database user run SQL with the rights of a function’s owner.',
        happenedSource: 'From the CVE records, shortened at a sentence end',
        affected: 'Yes', affectedNote: 'Platform Ops runs 17.9: three findings are inside their affected range, one cannot be checked.',
        todo: 'Update to 17.12', todoSource: 'The fixed release of the 17 branch, from the CVE records'
      },
      findings: [
        { id: 'f1', alert: 142, cve: 'CVE-2026-116325', title: 'Function owner’s rights through a crafted search path', cvss: 8.8,
          versionVerdict: 'in_range', range: '17.0 to before 17.12', fixed: '17.12', verdict: 'affected', verdictSource: 'rules', status: 'open',
          factors: [['CVSS 3.1 score 8.8', 3], ['No known exploitation', 0], ['17.9 is inside 17.0 to before 17.12', 1], ['Platform Ops is business-critical', 1], ['Internal network only', 0], ['None recorded', 0], ['None for this entry', 0], ['2.1%, below 50%', 0]] },
        { id: 'f2', alert: 144, cve: 'CVE-2026-118210', title: 'Memory disclosure in the replication protocol', cvss: 6.5,
          versionVerdict: 'in_range', range: '17.0 to before 17.11', fixed: '17.11', verdict: 'affected', verdictSource: 'rules', status: 'open',
          factors: [['CVSS 3.1 score 6.5', 2], ['No known exploitation', 0], ['17.9 is inside 17.0 to before 17.11', 1], ['Platform Ops is business-critical', 1], ['Internal network only', 0], ['None recorded', 0], ['None for this entry', 0], ['0.3%, below 50%', 0]] },
        { id: 'f3', alert: 145, cve: 'CVE-2026-118377', title: 'Crash through a malformed trigram index query', cvss: 7.1,
          versionVerdict: 'unknown', range: 'Not in structured data: “17 with pg_trgm enabled”', fixed: '17.12', verdict: 'uncertain', verdictSource: 'rules', status: 'open',
          factors: [['CVSS 3.1 score 7.1', 3], ['No known exploitation', 0], ['The record names no version range', 0], ['Platform Ops is business-critical', 1], ['Internal network only', 0], ['None recorded', 0], ['None for this entry', 0], ['0.6%, below 50%', 0]],
          model: { verdict: 'unlikely', likelihood: 'low', confidence: 'medium', match: ['The model judges it unlikely', -2],
            note: 'Needs the pg_trgm extension; the profile lists no extensions.' } },
        { id: 'f4', alert: 146, cve: 'CVE-2026-118402', title: 'Timing leak in SCRAM authentication', cvss: 3.7,
          versionVerdict: 'in_range', range: '17.0 to before 17.10', fixed: '17.10', verdict: 'affected', verdictSource: 'rules', status: 'open',
          factors: [['CVSS 3.1 score 3.7', 1], ['No known exploitation', 0], ['17.9 is inside 17.0 to before 17.10', 1], ['Platform Ops is business-critical', 1], ['Internal network only', 0], ['None recorded', 0], ['None for this entry', 0], ['0.1%, below 50%', 0]] }
      ],
      /* Checked and not affected: level Not relevant, no number, kept to answer “why no alert”. */
      checked: [
        { cve: 'CVE-2026-102244', title: 'Privilege escalation in logical replication', note: '17.9 is outside 17.0 to before 17.4' },
        { cve: 'CVE-2026-109930', title: 'Azure Database for PostgreSQL: token leak', note: 'A related product, not named as affected' }
      ],
      /* assess_event rows, newest first; the id is the event's id for notifications (master plan 8.2) */
      events: [
        { id: '0193f2a1-7c4e-7b2a-9d10-5e2f8c1a4b77', at: '2026-11-18T07:12', kind: 'finding.joined', text: 'CVE-2026-118402 joined the action', note: 'Low; no event, no notification: the level did not rise (8.2)' },
        { id: '0193f2a1-61d0-7f31-8a44-0c9b2e7d3a10', at: '2026-11-17T06:12', kind: 'notification.sent', text: 'Email sent to 3 people', note: 'Rule “High”, digest 08:00' },
        { id: '0193f2a1-5b8e-7c05-b1f2-7a6d4c9e2f31', at: '2026-11-17T06:10', kind: 'action.raised', change: [null, 'high'], text: 'Action raised at High with 3 findings', note: 'Reverse search after PostgreSQL 17.9 was read from the repository' }
      ],
      ai: {
        assessment: { alert: 142, engine: 'rules+model', model: 'qwen3:14b', provider: 'Ollama, local', prompt: 'v1', ruleset: 'v1', tokensIn: 2412, tokensOut: 438, seconds: 16, used: 2, quota: 5, at: '2026-11-17T06:14' },
        happened: 'A database user who may create objects can make PostgreSQL run their SQL with a function owner’s rights. Three more flaws are minor.',
        affected: 'Yes, likely', affectedNote: 'In range for three findings; the trigram flaw needs an extension the profile does not list.',
        actions: [
          { text: 'Update PostgreSQL to 17.12 on the primary and the replicas.', ref: 1 },
          { text: 'Until then, revoke CREATE on schema public from application roles.', ref: 2 },
          { text: 'Check whether pg_trgm is installed: SELECT * FROM pg_extension.', ref: null }
        ],
        preconditions: [
          { text: 'An account that may create functions or objects', state: 'met', ref: 1 },
          { text: 'pg_trgm enabled in a reachable database', state: 'unknown', ref: 3 },
          { text: 'Network access to port 5432', state: 'met', ref: null }
        ],
        impact: 'Full read and write access to the databases of Platform Ops.',
        unknowns: ['Whether pg_trgm is installed', 'Which roles may create objects'],
        reasoning: [
          { text: 'The flaw needs an authenticated user with CREATE rights; application roles usually have them on schema public', refs: [1, 2] },
          { text: 'The trigram crash applies only with pg_trgm, which the profile does not list', refs: [3] },
          { text: 'No exploitation is reported, and EPSS is low', refs: [4] }
        ],
        sources: [
          { n: 1, name: 'CVE record CVE-2026-116325', operator: 'CVE Program' },
          { n: 2, name: 'PostgreSQL security advisory 2026-11', operator: 'PostgreSQL Global Development Group' },
          { n: 3, name: 'CVE record CVE-2026-118377', operator: 'CVE Program' },
          { n: 4, name: 'EPSS scores of 17 Nov', operator: 'FIRST' }
        ]
      }
    },

    vite: {
      number: 157, title: 'Update vite to 7.1.11', project: 'Customer Portal', component: 'vite', installed: '7.1.5',
      target: '7.1.11', via: { name: '@angular/build', version: '22.2.0' },
      targetNote: 'vite comes in through @angular/build 22.2.0: updating @angular/build usually brings the fixed vite',
      status: 'in_review', assignee: 'lea', openedAt: '2026-11-16T10:40', due: '2027-02-14T10:40', exploitation: 'none', fixAvailable: true,
      development: true,
      answers: {
        happened: 'Two flaws in the vite development server. One lets a page read files outside the project while the dev server runs.',
        happenedSource: 'From the CVE records, shortened at a sentence end',
        affected: 'Yes, in development', affectedNote: 'vite 7.1.5 is in range; it is only used to build, so the level stays at Low.',
        todo: 'Update @angular/build', todoSource: 'Brings vite 7.1.11 or later; or pin vite to 7.1.11'
      },
      findings: [
        { id: 'v1', alert: 158, cve: 'CVE-2026-121004', title: 'Dev server serves files outside the root', cvss: 7.5,
          versionVerdict: 'in_range', range: '7.0.0 to before 7.1.11', fixed: '7.1.11', verdict: 'affected', verdictSource: 'rules', status: 'in_review',
          factors: [['CVSS 3.1 score 7.5', 3], ['No known exploitation', 0], ['7.1.5 is inside 7.0.0 to before 7.1.11', 1], ['Customer Portal: normal', 0], ['Internet-facing', 1], ['None recorded', 0], ['None for this entry', 0], ['1.2%, below 50%', 0]],
          cap: 'Development only: at most Low' },
        { id: 'v2', alert: 159, cve: 'CVE-2026-121187', title: 'Open redirect in the dev server’s base path', cvss: 4.3,
          versionVerdict: 'in_range', range: '7.0.0 to before 7.1.8', fixed: '7.1.8', verdict: 'affected', verdictSource: 'rules', status: 'in_review',
          factors: [['CVSS 3.1 score 4.3', 2], ['No known exploitation', 0], ['7.1.5 is inside 7.0.0 to before 7.1.8', 1], ['Customer Portal: normal', 0], ['Internet-facing', 1], ['None recorded', 0], ['None for this entry', 0], ['0.4%, below 50%', 0]],
          cap: 'Development only: at most Low' }
      ],
      checked: [],
      events: [
        { id: '0193f0c2-2e1a-7d40-a8b3-91c4e6f0d221', at: '2026-11-16T11:02', kind: 'action.status_changed', text: 'Lea Brunner took the action into review', note: 'Assigned to herself' },
        { id: '0193f0c2-1a77-7a19-9f02-3b8d5c7e1e04', at: '2026-11-16T10:40', kind: 'action.raised', change: [null, 'low'], text: 'Action raised at Low with 2 findings', note: 'Reverse search after commit 4f2a9c1 of customer-portal' }
      ]
    }
  };

  /* Totals and levels follow from the factors, never typed in twice. */
  Object.keys(REMEDIATIONS).forEach(function (key) {
    var r = REMEDIATIONS[key];
    r.findings.forEach(function (f) {
      f.points = points(f.factors);
      f.level = level(f.points);
      if (f.cap) f.level = 'low';
      if (f.model) {
        var withModel = f.points - f.factors[2][1] + f.model.match[1];
        /* The model lowers a finding by at most one level until a person confirms it. */
        var order = ['low', 'medium', 'high', 'critical'];
        var lowest = order[Math.max(0, order.indexOf(f.level) - 1)];
        f.model.points = withModel;
        f.model.level = order.indexOf(level(withModel)) < order.indexOf(lowest) ? lowest : level(withModel);
      }
    });
    r.level = r.findings.reduce(function (best, f) {
      var order = ['low', 'medium', 'high', 'critical'];
      return order.indexOf(f.level) > order.indexOf(best) ? f.level : best;
    }, 'low');
  });

  var P = REMEDIATIONS.postgres;
  console.assert(P.findings[0].points === 5 && P.findings[0].level === 'high', 'the top finding is High');
  console.assert(P.findings[2].level === 'medium' && P.findings[2].model.level === 'low', 'the model lowers by one');
  console.assert(P.level === 'high', 'the action takes the highest finding');
  console.assert(REMEDIATIONS.vite.level === 'low', 'development only stays Low');

  /* The alert behind finding f1, from the organization's view (master plan 7.4, 10.2) */
  var ALERT = {
    number: 142, cve: 'CVE-2026-116325', title: 'PostgreSQL: function owner’s rights through a crafted search path',
    cvss: { score: 8.8, version: '3.1', vector: 'CVSS:3.1/AV:N/AC:L/PR:L/UI:N/S:U/C:H/I:H/A:H' },
    epss: 0.021, exploitation: 'none', published: '2026-11-14T16:20', changed: '2026-11-17T05:40', firstRaised: '2026-11-17T06:10',
    answers: {
      happened: 'A database user who may create objects can run SQL with the rights of a function’s owner, through a crafted search path.',
      affected: 'Yes, 2 components in 1 project', affectedNote: 'Both PostgreSQL servers of Platform Ops are in range; the Customer Portal’s managed database is a related product.',
      todo: 'Update 17.9 to 17.12 and 16.10 to 16.14', todoSource: 'Fixed releases per branch, from the CVE record'
    },
    hits: [
      { project: 'Platform Ops', component: 'PostgreSQL', version: '17.9', note: 'Primary cluster', matchType: 'Product', strength: 'strong',
        versionVerdict: 'in_range', range: '17.0 to before 17.12', verdict: 'affected', action: 143, actionHref: 'remediation.html',
        factors: P.findings[0].factors },
      { project: 'Platform Ops', component: 'PostgreSQL', version: '16.10', note: 'Reporting replica', matchType: 'Product', strength: 'strong',
        versionVerdict: 'in_range', range: '16.0 to before 16.14', verdict: 'affected', action: 151, actionHref: 'remediation.html',
        factors: [['CVSS 3.1 score 8.8', 3], ['No known exploitation', 0], ['16.10 is inside 16.0 to before 16.14', 1], ['Reporting: normal', 0], ['Internal network only', 0], ['None recorded', 0], ['None for this entry', 0], ['2.1%, below 50%', 0]] },
      { project: 'Customer Portal', component: 'Azure Database for PostgreSQL', version: null, note: 'Managed service', matchType: 'Variant', strength: 'weak',
        versionVerdict: 'unknown', range: 'Not named as affected', verdict: 'not_affected', action: null,
        factors: null, why: 'A related product, not named as affected: Not relevant, no action' }
    ],
    absent: ['Webshop: no PostgreSQL in the profile'],
    sources: [
      { name: 'CVE record', operator: 'CVE Program', kind: 'Public mandate', note: 'Published 14 Nov, changed 17 Nov', href: 'https://www.cve.org/' },
      { name: 'PostgreSQL security advisory 2026-11', operator: 'PostgreSQL Global Development Group', kind: 'Vendor', note: 'Fixed releases 17.12, 16.14, 15.18' },
      { name: 'OSV', operator: 'Google', kind: 'Recognized body', note: 'Ranges per branch' },
      { name: 'EPSS', operator: 'FIRST', kind: 'Recognized body', note: '2.1% on 17 Nov' }
    ],
    history: [
      { at: '2026-11-14T16:20', label: 'Published', note: 'CVE Program' },
      { at: '2026-11-17T05:40', label: 'Ranges added', note: 'Vendor advisory, 3 branches' },
      { at: '2026-11-17T06:10', label: 'Raised', priority: 'high', note: '#143 and #151 opened' }
    ]
  };
  ALERT.hits.forEach(function (h) {
    if (h.factors) { h.points = points(h.factors); h.level = level(h.points); } else { h.level = 'none'; }
  });
  console.assert(ALERT.hits[0].level === 'high' && ALERT.hits[1].level === 'medium', 'alert levels per component');

  window.P3 = { FACTORS: FACTORS, REMEDIATIONS: REMEDIATIONS, ALERT: ALERT, level: level };
})();
