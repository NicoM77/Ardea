/* Ardea — planning-phase mockups.
   The sample dataset every screen renders from. Totals are computed from the rows, never typed in twice.
   Real: the CVE identifiers of the open actions and the two measured funnel figures (6 432 published records,
   173 candidates; planning spike of 11–17 Nov 2026). Invented: the organization, people, projects, installed
   versions, dates, bundle sizes, the closed actions and the national warning. */
(function () {
  'use strict';

  var LOADED = Date.now();
  /* ?state=calm: the same organization on a quiet day, nothing urgent open (task 3.1). */
  var CALM = /[?&]state=calm\b/.test(location.search);
  var NOW = new Date('2026-11-18T08:36:00');
  var HOUR = 3600e3, DAY = 24 * HOUR;

  /* Priority order follows the plan's levels 5 to 1; the word is always shown, never the number. */
  var PRIORITIES = {
    critical: { label: 'Critical', sym: 'p-critical', deadline: '48 hours', rank: 0 },
    high: { label: 'High', sym: 'p-high', deadline: '7 days', rank: 1 },
    medium: { label: 'Medium', sym: 'p-medium', deadline: '30 days', rank: 2 },
    low: { label: 'Low', sym: 'p-low', deadline: '90 days', rank: 3 },
    none: { label: 'Not relevant', sym: 'p-none', deadline: null, rank: 4 }
  };

  var STATUSES = {
    open: { label: 'Open', sym: 's-open' },
    in_review: { label: 'In review', sym: 's-review' },
    remediated: { label: 'Remediated', sym: 's-done', closing: 'How was it remediated?' },
    risk_accepted: { label: 'Risk accepted', sym: 's-accepted', closing: 'Why is the risk accepted?' },
    not_affected: { label: 'Not affected', sym: 's-not-affected', closing: 'Why is the project not affected?' }
  };

  var PEOPLE = {
    nina: { name: 'Nina Meier', initials: 'NM', role: 'Owner', hue: 'a' },
    marc: { name: 'Marc Keller', initials: 'MK', role: 'Admin', hue: 'b' },
    lea: { name: 'Lea Brunner', initials: 'LB', role: 'Member', hue: 'c' }
  };
  var ME = 'nina';

  var PROJECTS = [
    { id: 'platform-ops', name: 'Platform Ops', stack: ['Kubernetes', 'Linux', 'PostgreSQL', 'Puppet'], note: 'Business-critical' },
    { id: 'webshop', name: 'Webshop', stack: ['WordPress', 'PHP', 'MySQL'], note: 'Internet-facing' },
    { id: 'customer-portal', name: 'Customer Portal', stack: ['.NET', 'Angular', 'Umbraco', 'Azure SQL'], note: 'Internet-facing' }
  ];

  /* score: severity, exploitation, match, system criticality, exposure (rule set version 1; the rest are 0 here) */
  var ACTIONS = [
    { id: 'php', title: 'Update PHP to 8.3.36', component: 'PHP', installed: '8.3.30', target: '8.3.36', project: 'webshop',
      priority: 'critical', cve: 'CVE-2026-109619', findings: 1, exploited: true, score: [4, 3, 1, 0, 1],
      created: '2026-11-16T14:20', due: '2026-11-18T14:20', status: 'in_review', assignee: 'marc' },
    { id: 'puppet', title: 'Update Puppet Enterprise to 2025.12.1', component: 'Puppet Enterprise', installed: '2025.9.0', target: '2025.12.1', project: 'platform-ops',
      priority: 'critical', cve: 'CVE-2026-115731', findings: 1, exploited: true, score: [4, 3, 1, 1, 0],
      created: '2026-11-13T09:12', due: '2026-11-19T22:05', status: 'open', assignee: null },
    { id: 'postgres', title: 'Update PostgreSQL to 17.12', component: 'PostgreSQL', installed: '17.9', target: '17.12', project: 'platform-ops',
      priority: 'high', cve: 'CVE-2026-116325', findings: 4, score: [3, 0, 1, 1, 0],
      created: '2026-11-17T06:10', due: '2026-11-24T06:10', status: 'open', assignee: null },
    { id: 'mysql', title: 'Update MySQL Server', component: 'MySQL Server', installed: '8.4.11', target: null, project: 'webshop',
      priority: 'high', cve: 'CVE-2026-114952', findings: 6, score: [4, 0, 1, 0, 0],
      created: '2026-11-17T22:35', due: '2026-11-24T22:35', status: 'open', assignee: null },
    { id: 'umbraco', title: 'Update Umbraco CMS to 17.7.1', component: 'Umbraco CMS', installed: '17.5', target: '17.7.1', project: 'customer-portal',
      priority: 'medium', cve: 'CVE-2026-130068', findings: 1, score: [2, 0, 1, 0, 1],
      created: '2026-11-16T10:05', due: '2026-12-16T10:05', status: 'open', assignee: 'nina' },
    { id: 'ingress', title: 'Mitigate ingress-nginx', component: 'ingress-nginx', installed: '1.13.2', target: null, noFix: true, project: 'platform-ops',
      priority: 'low', cve: 'CVE-2026-116472', findings: 1, score: [1, 0, 1, 1, 0],
      created: '2026-11-12T11:30', due: '2027-02-10T11:30', status: 'open', assignee: null }
  ];

  if (CALM) ACTIONS = ACTIONS.filter(function (a) { return a.priority === 'medium' || a.priority === 'low'; });

  /* Closed in the last weeks. Invented history, so no CVE identifiers are shown for them. */
  var CLOSED = [
    { id: 'c-dotnet-2', title: 'Update .NET runtime to 10.0.4', project: 'customer-portal', priority: 'high', created: '2026-11-11T08:05', closed: '2026-11-11T17:40', status: 'remediated', assignee: 'nina' },
    { id: 'c-k8s', title: 'Update Kubernetes to 1.34.2', project: 'platform-ops', priority: 'low', created: '2026-10-26T10:15', closed: '2026-11-09T14:00', status: 'remediated', assignee: 'marc' },
    { id: 'c-woo', title: 'Update WooCommerce to 10.3.5', project: 'webshop', priority: 'high', created: '2026-11-04T07:30', closed: '2026-11-06T11:20', status: 'remediated', assignee: 'lea' },
    { id: 'c-kernel', title: 'Update Linux kernel to 6.12.58', project: 'platform-ops', priority: 'medium', created: '2026-10-23T09:40', closed: '2026-11-03T16:10', status: 'remediated', assignee: 'marc' },
    { id: 'c-angular', title: 'Update Angular to 21.0.4', project: 'customer-portal', priority: 'low', created: '2026-10-28T13:00', closed: '2026-10-29T09:30', status: 'not_affected', assignee: 'nina' },
    { id: 'c-openssl', title: 'Update OpenSSL to 3.5.5', project: 'platform-ops', priority: 'high', created: '2026-10-20T09:00', closed: '2026-10-22T16:30', status: 'remediated', assignee: 'marc' },
    { id: 'c-wp', title: 'Update WordPress to 6.8.4', project: 'webshop', priority: 'medium', created: '2026-10-14T08:20', closed: '2026-10-22T10:00', status: 'remediated', assignee: 'lea' },
    { id: 'c-dotnet-1', title: 'Update .NET runtime to 10.0.3', project: 'customer-portal', priority: 'high', created: '2026-10-14T08:10', closed: '2026-10-15T15:00', status: 'remediated', assignee: 'nina' }
  ];

  var FEED = [
    { at: '2026-11-17T22:35', kind: 'new', action: 'mysql' },
    { at: '2026-11-17T22:05', kind: 'raised', action: 'puppet', from: 'high', to: 'critical', why: 'CISA lists it as exploited' },
    { at: '2026-11-17T09:00', kind: 'warning', title: 'Phishing mails pose as vendor security updates', source: 'NCSC' },
    { at: '2026-11-17T06:10', kind: 'new', action: 'postgres' },
    { at: '2026-11-16T14:20', kind: 'new', action: 'php' }
  ];

  /* On a quiet day the changes are closures and checks that changed nothing. */
  if (CALM) FEED = [
    { at: '2026-11-18T07:50', kind: 'closed', title: 'Update PostgreSQL to 17.12', project: 'platform-ops', by: 'marc', status: 'remediated' },
    { at: '2026-11-17T17:10', kind: 'closed', title: 'Update PHP to 8.3.36', project: 'webshop', by: 'lea', status: 'remediated' },
    { at: '2026-11-17T09:00', kind: 'warning', title: 'Phishing mails pose as vendor security updates', source: 'NCSC' }
  ];

  /* Warnings without a vulnerability identifier (master plan 8.2, warning.published): national
     centres warn about campaigns and products before, or without, a CVE. Invented examples. */
  var WARNINGS = [
    { at: '2026-11-17T09:00', title: 'Phishing mails pose as vendor security updates', source: 'NCSC', operator: 'National Cyber Security Centre', kind: 'Authority',
      text: 'Mails with a vendor logo ask administrators to install an “urgent patch” from a lookalike domain.' },
    { at: '2026-11-15T14:30', title: 'Ransomware group targets unpatched VPN gateways', source: 'BACS', operator: 'Federal Office for Cyber Security', kind: 'Authority',
      text: 'Scans for edge devices without the October updates; no specific product or CVE named yet.' },
    { at: '2026-11-12T10:05', title: 'Fake invoices with HTML attachments', source: 'BACS', operator: 'Federal Office for Cyber Security', kind: 'Authority',
      text: 'Attachments open a login page that collects Microsoft 365 credentials.' }
  ];

  /* The sources and their state (master plan 5.6); health is a symbol and a word. */
  var SOURCE_LIST = [
    { name: 'CVE Program', health: 'healthy', synced: '2026-11-18T08:36' },
    { name: 'CISA KEV', health: 'healthy', synced: '2026-11-18T08:30' },
    { name: 'OSV', health: 'healthy', synced: '2026-11-18T08:20' },
    { name: 'BACS warnings', health: CALM ? 'healthy' : 'degraded', synced: CALM ? '2026-11-18T08:05' : '2026-11-17T22:00', note: CALM ? null : 'Last fetch returned no records' }
  ];
  var SOURCES = { count: SOURCE_LIST.length, synced: '2026-11-18T08:36' };

  /* The week's CVE records, 11–17 Nov 2026. The first two figures are measured, the rest follow from ACTIONS. */
  var FUNNEL = { from: '2026-11-11', to: '2026-11-17', published: 6432, candidates: 173 };

  /* The example vulnerability behind the Puppet action. */
  var VULN = {
    id: 'CVE-2026-115731', action: 'puppet',
    title: 'Puppet Enterprise: code execution through crafted reports',
    published: '2026-11-03T21:40', updated: '2026-11-17T22:05',
    cvss: { version: '4.0', score: 9.4, label: 'Critical',
      vector: 'CVSS:4.0/AV:N/AC:L/AT:N/PR:L/UI:N/VC:H/VI:H/VA:H/SC:H/SI:H/SA:H',
      /* level and of: how close the metric is to its worst value */
      exploit: [
        { name: 'Attack vector', value: 'Network', level: 4, of: 4 },
        { name: 'Attack complexity', value: 'Low', level: 2, of: 2 },
        { name: 'Attack requirements', value: 'None', level: 2, of: 2 },
        { name: 'Privileges required', value: 'Low', level: 2, of: 3 },
        { name: 'User interaction', value: 'None', level: 3, of: 3 }
      ],
      impact: [
        { name: 'Confidentiality', value: 'High', level: 3, of: 3 },
        { name: 'Integrity', value: 'High', level: 3, of: 3 },
        { name: 'Availability', value: 'High', level: 3, of: 3 },
        { name: 'Other systems', value: 'High', level: 3, of: 3 }
      ] },
    epss: 0.004,
    weaknesses: ['CWE-502 Deserialization of untrusted data', 'CWE-94 Code injection'],
    ssvc: ['Exploitation active', 'Not automatable', 'Total technical impact'],
    description: 'Deserialization of untrusted data in the report-processing code of Puppet, as used by Puppet Server, in the affected releases, allows a client that holds any CA-signed agent certificate to execute arbitrary code on the Puppet Server as its service user by submitting a report containing a crafted rich-data type via PUT request. Where the certificate authority autosigns requests without strong verification, an unauthenticated network attacker can obtain such a certificate.',
    /* at: where the installed version sits inside its branch, 0 to 1 */
    branches: [
      { from: '2023.8.0', fixed: '2023.8.13' },
      { from: '2025.0.0', fixed: '2025.12.1', installed: '2025.9.0', at: 9 / 12.1 },
      { from: '2026.0.0', fixed: '2026.0.1' }
    ],
    /* Rule set version 1. `then` is the value before CISA listed the flaw on 17 Nov. */
    factors: [
      { name: 'Severity', points: 4, then: 4, evidence: 'CVSS 4.0 score 9.4' },
      { name: 'Exploitation', points: 3, then: 0, evidence: 'In the CISA KEV catalog since 17 Nov', evidenceThen: 'No known exploitation' },
      { name: 'Match', points: 1, then: 1, evidence: 'Installed 2025.9.0 is in the affected range' },
      { name: 'System criticality', points: 1, then: 1, evidence: 'Platform Ops is business-critical' },
      { name: 'Exposure', points: 0, then: 0, evidence: 'Internal network only' },
      { name: 'Protective measure', points: 0, then: 0, evidence: 'None recorded' },
      { name: 'National warning', points: 0, then: 0, evidence: 'None for this entry' },
      { name: 'EPSS', points: 0, then: 0, evidence: '0.4%, below the 50% threshold' }
    ],
    /* Sum to level: 3 and less Low, 4 Medium, 5 to 7 High, 8 and more Critical. The scale ends at the highest possible sum. */
    bands: [
      { priority: 'low', from: 0, to: 3 }, { priority: 'medium', from: 4, to: 4 },
      { priority: 'high', from: 5, to: 7 }, { priority: 'critical', from: 8, to: 12 }
    ],
    projects: [
      { project: 'platform-ops', priority: 'critical', note: 'Puppet Enterprise 2025.9.0', action: 'puppet' },
      { project: 'webshop', priority: 'none', note: 'No Puppet in the profile' },
      { project: 'customer-portal', priority: 'none', note: 'No Puppet in the profile' }
    ],
    sources: [
      { name: 'CVE Program', note: 'Record by Perforce, changed 17 Nov', href: 'https://www.cve.org/CVERecord?id=CVE-2026-115731' },
      { name: 'CISA Known Exploited Vulnerabilities', note: 'Added 17 Nov', href: 'https://www.cisa.gov/known-exploited-vulnerabilities-catalog' },
      { name: 'Vendor advisory, Perforce', note: 'Linked from the CVE record' },
      { name: 'FIRST EPSS', note: 'Score of 17 Nov', href: 'https://www.first.org/epss/' }
    ],
    history: [
      { at: '2026-11-03T21:40', label: 'Published', note: 'CVE Program' },
      { at: '2026-11-13T09:12', label: 'Assessed', priority: 'high', note: 'Puppet added to Platform Ops' },
      { at: '2026-11-17T22:05', label: 'Raised', priority: 'critical', note: 'CISA lists it as exploited' }
    ]
  };

  var ACTIVITY = [
    { at: '2026-11-17T22:07', text: 'Email and push sent to 3 people', note: 'Rule “Critical at once”' },
    { at: '2026-11-17T22:05', change: ['high', 'critical'], text: 'CISA lists the flaw as exploited', note: 'Due date moved from 20 Nov to 19 Nov' },
    { at: '2026-11-13T09:12', text: 'Action created at High', note: 'Puppet Enterprise 2025.9.0 added to Platform Ops' }
  ];

  /* ---------- derived values ---------- */
  function date(v) { return v instanceof Date ? v : new Date(v); }
  function now() { return new Date(NOW.getTime() + (Date.now() - LOADED)); }
  function sum(list, f) { return list.reduce(function (acc, x) { return acc + f(x); }, 0); }
  function byId(list, id) { return list.filter(function (x) { return x.id === id; })[0]; }

  ACTIONS.forEach(function (a) { a.created = date(a.created); a.due = date(a.due); a.total = sum(a.score, Number); a.href = a.id === 'postgres' ? 'remediation.html' : 'action.html'; });
  CLOSED.forEach(function (a) { a.created = date(a.created); a.closed = date(a.closed); a.days = (a.closed - a.created) / DAY; });
  ACTIONS.sort(function (a, b) { return a.due - b.due; });

  function count(list, f) { return list.filter(f).length; }
  var byPriority = ['critical', 'high', 'medium', 'low'].map(function (p) {
    return { priority: p, count: count(ACTIONS, function (a) { return a.priority === p; }) };
  });

  FUNNEL.matched = sum(ACTIONS, function (a) { return a.findings; });
  FUNNEL.actions = ACTIONS.length;
  FUNNEL.dismissed = FUNNEL.candidates - FUNNEL.matched;

  /* Open actions at the end of each of the last 31 days, from the created and closed dates above. */
  function trend(days) {
    var out = [], end = new Date(NOW); end.setHours(23, 59, 59, 999);
    for (var i = days - 1; i >= 0; i--) {
      var t = new Date(end); t.setDate(end.getDate() - i);   /* calendar days: the window crosses the end of summer time */
      var at = i === 0 ? NOW : t;
      var open = count(ACTIONS, function (a) { return a.created <= at; }) +
        count(CLOSED, function (a) { return a.created <= at && a.closed > at; });
      out.push({ date: t, open: open });
    }
    return out;
  }

  function median(values) {
    var s = values.slice().sort(function (a, b) { return a - b; }), m = Math.floor(s.length / 2);
    return s.length ? (s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2) : null;
  }
  var remediated = CLOSED.filter(function (a) { return a.status === 'remediated'; });

  /* ---------- formats ---------- */
  var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  function pad(n) { return (n < 10 ? '0' : '') + n; }
  function day(d) { d = date(d); return d.getDate() + ' ' + MONTHS[d.getMonth()]; }
  function clock(d) { d = date(d); return pad(d.getHours()) + ':' + pad(d.getMinutes()); }
  function dayTime(d) { return day(d) + ', ' + clock(d); }
  function sameDay(a, b) { return a.toDateString() === b.toDateString(); }
  /* Recency in words: Today, Yesterday, then the date. */
  function when(d) {
    d = date(d);
    if (sameDay(d, NOW)) return 'Today ' + clock(d);
    if (sameDay(d, new Date(NOW.getTime() - DAY))) return 'Yesterday ' + clock(d);
    if (sameDay(d, new Date(NOW.getTime() + DAY))) return 'Tomorrow ' + clock(d);
    return dayTime(d);
  }
  function int(n) { return String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ','); }
  /* Time left until a deadline: hours and minutes inside two days, whole days after that. */
  function left(due, from) {
    var ms = date(due) - (from || now());
    var over = ms < 0; ms = Math.abs(ms);
    var d = Math.floor(ms / DAY), h = Math.floor((ms % DAY) / HOUR), m = Math.floor((ms % HOUR) / 60e3);
    var text = ms < DAY ? h + ' h ' + pad(m) + ' m' : ms < 2 * DAY ? d + ' d ' + h + ' h' : d + ' d';
    return { over: over, text: text, ms: ms, d: d, h: h, m: m, s: Math.floor((ms % 60e3) / 1e3) };
  }
  function duration(days) { return days < 1 ? Math.max(1, Math.round(days * 24)) + ' h' : (Math.round(days * 10) / 10).toString().replace(/\.0$/, '') + ' d'; }

  /* ---------- the figures that must agree ---------- */
  console.assert(sum(byPriority, function (p) { return p.count; }) === ACTIONS.length, 'priority counts sum to the open actions');
  console.assert(CALM || (FUNNEL.matched === 14 && FUNNEL.matched + FUNNEL.dismissed === FUNNEL.candidates), 'funnel stages add up');
  console.assert(CALM || byId(ACTIONS, 'puppet').total === sum(VULN.factors, function (f) { return f.points; }), 'Puppet score equals its factors');
  console.assert(sum(PROJECTS, function (p) { return count(ACTIONS, function (a) { return a.project === p.id; }); }) === ACTIONS.length, 'every action belongs to a project');
  console.assert(trend(31).slice(-1)[0].open === ACTIONS.length, 'the trend ends at the open count');
  console.assert(ACTIONS.every(function (a) {
    var t = a.total, p = t >= 8 ? 'critical' : t >= 5 ? 'high' : t === 4 ? 'medium' : 'low';
    return p === a.priority;
  }), 'every priority follows from its score');

  window.ARDEA = {
    NOW: NOW, now: now, DAY: DAY, HOUR: HOUR, ME: ME,
    PRIORITIES: PRIORITIES, STATUSES: STATUSES, PEOPLE: PEOPLE, PROJECTS: PROJECTS,
    CALM: CALM, WARNINGS: WARNINGS, SOURCE_LIST: SOURCE_LIST,
    ACTIONS: ACTIONS, CLOSED: CLOSED, FEED: FEED, SOURCES: SOURCES, FUNNEL: FUNNEL, VULN: VULN, ACTIVITY: ACTIVITY,
    byPriority: byPriority, trend: trend, remediated: remediated,
    medianFixDays: median(remediated.map(function (a) { return a.days; })),
    action: function (id) { return byId(ACTIONS, id); },
    project: function (id) { return byId(PROJECTS, id); },
    count: count, sum: sum,
    fmt: { day: day, clock: clock, dayTime: dayTime, when: when, int: int, left: left, duration: duration }
  };
})();
