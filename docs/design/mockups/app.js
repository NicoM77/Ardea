/* Ardea — planning-phase mockups.
   The shared shell (icons, sidebar, top bar, command palette) and the small helpers every page uses.
   Motion rules: transform and opacity only, a strong ease-out for anything arriving, nothing on
   keyboard-initiated actions, and everything skipped when the visitor prefers reduced motion. */
(function () {
  'use strict';

  var D = window.ARDEA;
  var A = window.A = {};

  /* ?static draws every chart in its final state: used for screenshots and by reduced motion */
  A.reduced = /[?&]static\b/.test(location.search) ||
    (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  if (/[?&]static\b/.test(location.search)) document.documentElement.classList.add('static');

  A.EASE_OUT = 'cubic-bezier(0.23, 1, 0.32, 1)';
  A.EASE_IN_OUT = 'cubic-bezier(0.77, 0, 0.175, 1)';

  /* ---------- Icons: one sprite, referenced with <use href="#name"> ---------- */
  var WHITE = 'fill="#fff" stroke="none"';
  var ICONS = {
    overview: '<rect x="3.5" y="3.5" width="7" height="7" rx="1.8"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.8"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.8"/><rect x="13.5" y="13.5" width="7" height="7" rx="1.8"/>',
    actions: '<path d="m4 6.5 1.6 1.6L8.5 5M4 12.5l1.6 1.6 2.9-3.1M4 18.5l1.6 1.6 2.9-3.1M11.5 7h8.5M11.5 13h8.5M11.5 19h8.5"/>',
    vulnerabilities: '<path d="M12 3 5 6v5.5c0 4.3 2.9 8 7 9.5 4.1-1.5 7-5.2 7-9.5V6l-7-3Z"/><path d="M12 8v4.5M12 15.8v.2"/>',
    projects: '<path d="M3.5 7.5a2 2 0 0 1 2-2h3.8l2 2h7.2a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2v-10Z"/>',
    sources: '<circle cx="12" cy="12" r="1.8"/><path d="M8.2 15.8a5.4 5.4 0 0 1 0-7.6M15.8 8.2a5.4 5.4 0 0 1 0 7.6M5.4 18.6a9.3 9.3 0 0 1 0-13.2M18.6 5.4a9.3 9.3 0 0 1 0 13.2"/>',
    rules: '<path d="M6 16.5V11a6 6 0 1 1 12 0v5.5l1.5 2h-15l1.5-2ZM10 20.5a2.2 2.2 0 0 0 4 0"/>',
    team: '<circle cx="9" cy="8.5" r="3.2"/><path d="M3.5 19.5c.6-3.2 2.8-5 5.5-5s4.9 1.8 5.5 5M15.5 5.6a3.2 3.2 0 0 1 0 5.8M17 14.8c2 .6 3.2 2.3 3.5 4.7"/>',
    settings: '<path d="M4 7h9M17 7h3M4 17h3M11 17h9M15 4.5v5M9 14.5v5"/>',
    design: '<circle cx="12" cy="12" r="8.5"/><path d="M12 3.5v17M3.5 12h17"/>',
    'chevron-right': '<path d="m9.5 5.5 6.5 6.5-6.5 6.5"/>',
    'chevron-left': '<path d="M14.5 5.5 8 12l6.5 6.5"/>',
    'chevron-updown': '<path d="m8 9 4-4 4 4M8 15l4 4 4-4"/>',
    'arrow-right': '<path d="M5 12h14M13 6l6 6-6 6"/>',
    'arrow-up': '<path d="M12 19V5M6 11l6-6 6 6"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    search: '<circle cx="10.5" cy="10.5" r="6"/><path d="m15 15 5 5"/>',
    check: '<path d="m5 12.5 4.5 4.5L19 7"/>',
    'check-circle': '<circle cx="12" cy="12" r="8.5"/><path d="m8.2 12.3 2.6 2.6 5-5.4"/>',
    info: '<circle cx="12" cy="12" r="8.5"/><path d="M12 11v5.2M12 7.8v.2"/>',
    external: '<path d="M14 4.5h5.5V10M19.5 4.5 11 13M17 14v4.5a1 1 0 0 1-1 1H5.5a1 1 0 0 1-1-1V8a1 1 0 0 1 1-1H10"/>',
    copy: '<rect x="8.5" y="8.5" width="11" height="11" rx="2"/><path d="M15.5 8.5v-2a2 2 0 0 0-2-2h-7a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h2"/>',
    replay: '<path d="M4.5 12a7.5 7.5 0 1 0 2.4-5.5"/><path d="M4.5 4.5v4h4"/>',
    menu: '<path d="M4 7h16M4 12h16M4 17h16"/>',
    close: '<path d="m6 6 12 12M18 6 6 18"/>',
    branch: '<circle cx="7" cy="5.5" r="2"/><circle cx="7" cy="18.5" r="2"/><circle cx="17" cy="7.5" r="2"/><path d="M7 7.5v9M17 9.5c0 4.2-4.5 4.2-9.2 7.4"/>',
    repo: '<path d="M5.5 18.5v-13a2 2 0 0 1 2-2h11v14h-11a2 2 0 0 0-2 2Zm0 0a2 2 0 0 0 2 2h11"/>',
    route: '<circle cx="6" cy="18" r="2"/><circle cx="18" cy="6" r="2"/><path d="M8 18h7.5a3 3 0 0 0 0-6h-7a3 3 0 0 1 0-6H16"/>',
    file: '<path d="M13.5 3.5h-6a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2v-10l-5-5Z"/><path d="M13.5 3.5v5h5"/>',
    megaphone: '<path d="M4.5 10v4l10 4.5v-13L4.5 10Z"/><path d="M7.5 15.4v2.4a1.7 1.7 0 0 0 3.4 0v-.9M18 9.5a3.5 3.5 0 0 1 0 5"/>',
    /* Priority symbols: shape, colour and word, never colour alone */
    'p-critical': '<path d="M7.4 1.5h9.2l5.9 5.9v9.2l-5.9 5.9H7.4l-5.9-5.9V7.4l5.9-5.9Z" fill="currentColor"/><rect x="10.9" y="6.6" width="2.2" height="7.6" rx="1.1" ' + WHITE + '/><circle cx="12" cy="17" r="1.3" ' + WHITE + '/>',
    'p-high': '<path d="M10.3 3.6a2 2 0 0 1 3.4 0l8 13.9a2 2 0 0 1-1.7 3H4a2 2 0 0 1-1.7-3l8-13.9Z" fill="currentColor"/><rect x="10.9" y="8.4" width="2.2" height="6.6" rx="1.1" ' + WHITE + '/><circle cx="12" cy="17.6" r="1.25" ' + WHITE + '/>',
    'p-medium': '<path d="M12 1.2 22.8 12 12 22.8 1.2 12 12 1.2Z" fill="currentColor"/><rect x="10.9" y="6.8" width="2.2" height="7" rx="1.1" ' + WHITE + '/><circle cx="12" cy="16.6" r="1.25" ' + WHITE + '/>',
    'p-low': '<circle cx="12" cy="12" r="8.6" stroke-width="2.2"/><path d="M12 7.6v8.4M8.4 12.6l3.6 3.6 3.6-3.6" stroke-width="2.2"/>',
    'p-none': '<circle cx="12" cy="12" r="8.6" stroke-width="2.2"/><path d="M8 12h8" stroke-width="2.2"/>',
    /* Working states: neutral, told apart by shape */
    's-open': '<circle cx="12" cy="12" r="8.4" stroke-width="2.2"/>',
    's-review': '<circle cx="12" cy="12" r="8.4" stroke-width="2.2"/><path d="M12 6.4a5.6 5.6 0 0 1 0 11.2Z" fill="currentColor" stroke="none"/>',
    's-done': '<circle cx="12" cy="12" r="9.5" fill="currentColor" stroke="none"/><path d="m8 12.3 2.8 2.8 5.2-5.6" stroke="#fff" stroke-width="2.2"/>',
    's-accepted': '<path d="M5.5 21V4.5M5.5 4.5h12l-2.6 4.2 2.6 4.3h-12" stroke-width="2.2"/>',
    's-not-affected': '<circle cx="12" cy="12" r="8.4" stroke-width="2.2"/><path d="m6.2 17.8 11.6-11.6" stroke-width="2.2"/>'
  };

  function sprite() {
    var out = '<svg xmlns="http://www.w3.org/2000/svg" style="display:none">';
    Object.keys(ICONS).forEach(function (name) {
      out += '<symbol id="i-' + name + '" viewBox="0 0 24 24">' + ICONS[name] + '</symbol>';
    });
    return out + '</svg>';
  }

  /* ---------- Small builders ---------- */
  A.esc = function (s) {
    return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; });
  };
  A.icon = function (name) { return '<svg class="icon" aria-hidden="true"><use href="#i-' + name + '"/></svg>'; };
  A.sym = function (name) { return '<svg class="sym" aria-hidden="true"><use href="#i-' + name + '"/></svg>'; };
  A.prio = function (p, extra) {
    var def = D.PRIORITIES[p];
    return '<span class="prio prio--' + p + (extra ? ' ' + extra : '') + '">' + A.sym(def.sym) + def.label + '</span>';
  };
  A.status = function (s) {
    var def = D.STATUSES[s];
    return '<span class="status status--' + s + '">' + A.sym(def.sym) + def.label + '</span>';
  };
  A.who = function (id) {
    var p = D.PEOPLE[id];
    return p ? '<span class="who who--' + p.hue + '" data-tip="' + A.esc(p.name) + '" aria-hidden="true">' + p.initials + '</span>'
      : '<span class="who who--none" data-tip="Unassigned" aria-hidden="true"></span>';
  };
  A.owner = function (id) {
    var p = D.PEOPLE[id];
    return '<span class="owner">' + A.who(id) + '<span' + (p ? '' : ' class="muted"') + '>' + (p ? A.esc(p.name) : 'Unassigned') + '</span></span>';
  };
  A.$ = function (sel, root) { return (root || document).querySelector(sel); };
  A.$$ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };

  /* ---------- Motion helpers ---------- */
  /* Runs fn once, when el first comes into view */
  A.onView = function (el, fn) {
    if (!('IntersectionObserver' in window) || A.reduced) { fn(); return; }
    var io = new IntersectionObserver(function (entries) {
      if (entries.some(function (e) { return e.isIntersecting; })) { io.disconnect(); fn(); }
    }, { threshold: 0.2 });
    io.observe(el);
  };
  /* A bar growing from its left edge; scaleX, never width */
  A.grow = function (el, delay, duration) {
    if (A.reduced || !el.animate) return;
    el.animate([{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }],
      { duration: duration || 600, delay: delay || 0, easing: A.EASE_OUT, fill: 'backwards' });
  };
  A.fadeIn = function (el, delay, duration) {
    if (A.reduced || !el.animate) return;
    el.animate([{ opacity: 0 }, { opacity: 1 }], { duration: duration || 300, delay: delay || 0, easing: 'ease', fill: 'backwards' });
  };
  /* A number counting up to its value, once */
  A.countUp = function (el, to, opts) {
    opts = opts || {};
    var format = opts.format || D.fmt.int;
    if (A.reduced) { el.textContent = format(to); return; }
    var from = opts.from || 0, start = null, dur = opts.duration || 700, delay = opts.delay || 0;
    var run = el._count = (el._count || 0) + 1;   /* a newer count on the same element takes over */
    el.textContent = format(from);
    function frame(t) {
      if (el._count !== run) return;
      if (start === null) start = t + delay;
      var k = Math.min(1, Math.max(0, (t - start) / dur));
      var eased = 1 - Math.pow(1 - k, 4);
      el.textContent = format(from + (to - from) * eased);
      if (k < 1) requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
    /* Frames pause in a background tab; the value must be right when the tab is opened */
    setTimeout(function () { if (el._count === run) { el._count = run + 1; el.textContent = format(to); } }, delay + dur + 200);
  };
  /* One indicator that slides to the selected tab or segment */
  A.slider = function (track, ink, selected) {
    function place(animate) {
      var el = A.$(selected, track);
      if (!el) return;
      if (!animate) ink.style.transition = 'none';
      ink.style.transform = 'translateX(' + el.offsetLeft + 'px) scaleX(' + (el.offsetWidth / 100) + ')';
      if (!animate) { void ink.offsetWidth; ink.style.transition = ''; }
    }
    place(false);
    window.addEventListener('resize', function () { place(false); });
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(function () { place(false); });
    return place;
  };
  /* A change inside the page (filtering, reordering) as one view transition, when the browser has them */
  A.transition = function (fn) {
    if (A.reduced || !document.startViewTransition) { fn(); return; }
    document.startViewTransition(fn);
  };

  /* ---------- Tooltip: a short delay for the first, none for the next ---------- */
  var tipEl, tipTimer, tipWarm = 0;
  A.tip = {
    show: function (html, x, y, now) {
      if (!tipEl) { tipEl = document.createElement('div'); tipEl.className = 'tip'; tipEl.setAttribute('role', 'tooltip'); document.body.appendChild(tipEl); }
      clearTimeout(tipTimer);
      var open = function () {
        tipEl.innerHTML = html;
        var w = tipEl.offsetWidth, h = tipEl.offsetHeight;
        var left = Math.max(8, Math.min(window.innerWidth - w - 8, x - w / 2));
        var top = y - h - 10 < 8 ? y + 16 : y - h - 10;
        tipEl.style.transform = 'translate(' + Math.round(left) + 'px,' + Math.round(top) + 'px)';
        tipEl.classList.add('show');
      };
      if (now || Date.now() - tipWarm < 400 || tipEl.classList.contains('show')) open();
      else tipTimer = setTimeout(open, 350);
    },
    hide: function () {
      clearTimeout(tipTimer);
      if (tipEl && tipEl.classList.contains('show')) { tipEl.classList.remove('show'); tipWarm = Date.now(); }
    }
  };
  document.addEventListener('pointerover', function (e) {
    if (e.pointerType === 'touch') return;
    var el = e.target.closest && e.target.closest('[data-tip]');
    if (!el) return;
    var r = el.getBoundingClientRect();
    A.tip.show(el.getAttribute('data-tip'), r.left + r.width / 2, r.top);
  });
  document.addEventListener('pointerout', function (e) {
    var el = e.target.closest && e.target.closest('[data-tip]');
    if (el && !el.contains(e.relatedTarget)) A.tip.hide();
  });

  /* ---------- Toast: confirms a change in place, with Undo ---------- */
  var toastEl, toastTimer;
  A.toast = function (text, opts) {
    opts = opts || {};
    if (!toastEl) {
      toastEl = document.createElement('div');
      toastEl.className = 'toast is-hidden';
      toastEl.setAttribute('role', 'status');
      document.body.appendChild(toastEl);
      void toastEl.offsetWidth;
    }
    clearTimeout(toastTimer);
    toastEl.innerHTML = (opts.plain ? '' : A.icon('check-circle')) + '<span>' + A.esc(text) + '</span>' +
      (opts.undo ? '<button class="btn" type="button">Undo</button>' : '');
    toastEl.classList.remove('is-hidden');
    var btn = A.$('button', toastEl);
    if (btn) btn.addEventListener('click', function () { A.toast.hide(); opts.undo(); });
    toastTimer = setTimeout(A.toast.hide, opts.undo ? 7000 : 3200);
  };
  A.toast.hide = function () { clearTimeout(toastTimer); if (toastEl) toastEl.classList.add('is-hidden'); };

  /* ---------- Shell ---------- */
  var ME = D.PEOPLE[D.ME];
  var NAV = [
    { id: 'overview', label: 'Overview', href: 'overview.html', icon: 'overview' },
    { id: 'actions', label: 'Actions', href: 'actions.html', icon: 'actions', count: D.ACTIONS.length },
    { id: 'vulnerabilities', label: 'Vulnerabilities', icon: 'vulnerabilities' },
    { id: 'projects', label: 'Projects', href: 'project.html', icon: 'projects' },
    { id: 'sources', label: 'Sources', href: 'sources.html', icon: 'sources' },
    { id: 'rules', label: 'Notification rules', icon: 'rules' }
  ];
  var NAV_FOOT = [
    { id: 'team', label: 'Team', icon: 'team' },
    { id: 'settings', label: 'Settings', icon: 'settings' },
    { id: 'setup', label: 'Setup wizard', href: 'setup-wizard.html', icon: 'route' },
    { id: 'design-system', label: 'Design system', href: 'design-system.html', icon: 'design' }
  ];

  function navLinks(items, current) {
    return items.map(function (item) {
      var inner = A.icon(item.icon) + '<span>' + item.label + '</span>' +
        (item.count ? '<span class="nav-count">' + item.count + '<span class="sr-only"> open</span></span>' : '');
      if (!item.href) return '<button type="button" data-soon="' + item.label + '">' + inner + '</button>';
      return '<a href="' + item.href + '"' + (item.id === current ? ' aria-current="page"' : '') + '>' + inner + '</a>';
    }).join('');
  }

  function buildShell() {
    var body = document.body;
    var section = body.getAttribute('data-section') || body.getAttribute('data-page');
    var sidebar =
      '<nav class="sidebar" id="sidebar" aria-label="Main">' +
        '<div class="brand"><a href="overview.html"><img src="../brand/ardea-logo.svg" alt="Ardea" width="85" height="26"></a>' +
          '<button class="btn ghost icon-only sidebar-close" type="button" aria-label="Close menu">' + A.icon('close') + '</button></div>' +
        '<button class="org-switch" type="button" data-soon="Switching organizations" aria-label="Organization: Example Digital AG. Switch organization">' +
          '<span class="org-mark" aria-hidden="true">E</span>' +
          '<span class="org-name">Example Digital AG<small>' + D.PROJECTS.length + ' projects</small></span>' + A.icon('chevron-updown') + '</button>' +
        '<div class="nav">' + navLinks(NAV, section) + '</div>' +
        '<div class="sidebar-foot nav">' + navLinks(NAV_FOOT, section) +
          '<div class="sync"><span class="live" aria-hidden="true"></span>' + D.SOURCES.count + ' sources synced at ' + D.fmt.clock(D.SOURCES.synced) + '</div>' +
          '<div class="user"><span class="who who--' + ME.hue + '" aria-hidden="true">' + ME.initials + '</span><span class="user-name">' + ME.name + '<small>' + ME.role + '</small></span></div>' +
        '</div>' +
      '</nav>';
    var topbar =
      '<header class="topbar">' +
        '<button class="btn ghost icon-only menu-toggle" type="button" aria-label="Open menu" aria-controls="sidebar">' + A.icon('menu') + '</button>' +
        '<img class="brand-mark" src="../brand/ardea-mark.svg" alt="Ardea" width="24" height="24">' +
        '<button class="search-trigger" type="button" data-palette>' + A.icon('search') + '<span>Search actions, CVEs, pages</span><kbd>/</kbd></button>' +
        '<button class="account" type="button" data-soon="The account menu" aria-label="Account: ' + ME.name + '"><span class="who who--' + ME.hue + '" aria-hidden="true">' + ME.initials + '</span></button>' +
      '</header>';
    body.insertAdjacentHTML('afterbegin', '<a class="skip-link" href="#main">Skip to content</a>' + sprite() + sidebar + topbar);

    var side = document.getElementById('sidebar');
    A.$('.menu-toggle').addEventListener('click', function () { side.classList.add('open'); A.$('.sidebar-close').focus(); });
    A.$('.sidebar-close').addEventListener('click', function () { side.classList.remove('open'); A.$('.menu-toggle').focus(); });

    /* Destinations that are not drawn yet say so instead of doing nothing */
    document.addEventListener('click', function (e) {
      var soon = e.target.closest('[data-soon]');
      if (soon) A.toast(soon.getAttribute('data-soon') + ' is not part of this mockup yet', { plain: true });
    });

    /* A row's title travels into the next page's title */
    document.addEventListener('click', function (e) {
      var link = e.target.closest('a[data-morph]');
      if (!link || A.reduced) return;
      var title = A.$('[data-title]', link), h1 = A.$('h1');
      if (!title) return;
      if (h1) h1.style.viewTransitionName = 'none';
      title.style.viewTransitionName = 'page-title';
    });
    window.addEventListener('pageshow', function (e) {
      if (!e.persisted) return;
      A.$$('[data-title]').forEach(function (el) { el.style.viewTransitionName = ''; });
      var h1 = A.$('h1'); if (h1) h1.style.viewTransitionName = '';
    });

    A.$$('.rise').forEach(function (el, i) { el.style.setProperty('--i', Math.min(i, 8)); });
  }

  /* ---------- Command palette ---------- */
  function paletteItems() {
    var items = [
      { group: 'Pages', label: 'Overview', href: 'overview.html', icon: 'overview' },
      { group: 'Pages', label: 'Actions', href: 'actions.html', icon: 'actions' },
      { group: 'Pages', label: 'Customer Portal (project)', href: 'project.html', icon: 'projects' },
      { group: 'Pages', label: 'Sources', href: 'sources.html', icon: 'sources' },
      { group: 'Pages', label: 'Setup wizard', href: 'setup-wizard.html', icon: 'route' },
      { group: 'Pages', label: 'Design system', href: 'design-system.html', icon: 'design' }
    ];
    D.ACTIONS.forEach(function (a) {
      items.push({ group: 'Open actions', label: a.title, href: a.href, sym: D.PRIORITIES[a.priority].sym, priority: a.priority,
        note: D.project(a.project).name, search: a.title + ' ' + a.cve + ' ' + a.component + ' ' + D.project(a.project).name });
    });
    items.push({ group: 'Vulnerabilities', label: D.VULN.id + ' · ' + D.VULN.title, href: 'vulnerability.html', sym: 'p-critical', priority: 'critical', search: D.VULN.id + ' ' + D.VULN.title });
    return items;
  }

  var palette = null;
  function openPalette(byPointer) {
    if (palette) return;
    var all = paletteItems(), shown = all, index = 0, opener = document.activeElement;
    var back = document.createElement('div');
    /* Opened from the keyboard it simply appears; opened with the pointer it scales in */
    back.className = 'palette-back' + (byPointer && !A.reduced ? ' by-pointer' : '');
    back.innerHTML = '<div class="palette" role="dialog" aria-modal="true" aria-label="Search">' +
      '<label class="palette-input">' + A.icon('search') + '<span class="sr-only">Search</span>' +
      '<input type="text" placeholder="Search actions, CVEs, pages" autocomplete="off" spellcheck="false" role="combobox" aria-expanded="true" aria-controls="palette-list"><kbd>Esc</kbd></label>' +
      '<div class="palette-list" id="palette-list" role="listbox"></div></div>';
    document.body.appendChild(back);
    palette = back;
    var input = A.$('input', back), list = A.$('.palette-list', back);

    function render() {
      var group = '', html = '';
      shown.forEach(function (it, i) {
        if (it.group !== group) { group = it.group; html += '<h3>' + group + '</h3>'; }
        html += '<div class="palette-item' + (it.priority ? ' c-' + it.priority : '') + '" role="option" data-i="' + i + '" aria-selected="' + (i === index) + '">' +
          (it.sym ? A.sym(it.sym) : A.icon(it.icon)) + '<span>' + A.esc(it.label) + '</span>' + (it.note ? '<small>' + A.esc(it.note) + '</small>' : '') + '</div>';
      });
      list.innerHTML = html || '<p class="palette-empty">Nothing matches “' + A.esc(input.value) + '”.</p>';
      var sel = A.$('[aria-selected="true"]', list);
      if (sel) sel.scrollIntoView({ block: 'nearest' });
    }
    function close() { back.remove(); palette = null; if (opener && opener.focus) opener.focus(); }
    function go(it) { if (it) location.href = it.href; }

    input.addEventListener('input', function () {
      var q = input.value.trim().toLowerCase();
      shown = all.filter(function (it) { return !q || (it.search || it.label).toLowerCase().indexOf(q) !== -1; });
      index = 0; render();
    });
    input.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        if (shown.length) index = (index + (e.key === 'ArrowDown' ? 1 : shown.length - 1)) % shown.length;
        render();
      } else if (e.key === 'Enter') { e.preventDefault(); go(shown[index]); }
      else if (e.key === 'Escape') { e.preventDefault(); close(); }
      else if (e.key === 'Tab') e.preventDefault();
    });
    list.addEventListener('click', function (e) {
      var item = e.target.closest('.palette-item');
      if (item) go(shown[+item.getAttribute('data-i')]);
    });
    back.addEventListener('pointerdown', function (e) { if (e.target === back) close(); });
    render();
    input.focus();
  }

  function initPalette() {
    document.addEventListener('click', function (e) {
      if (e.target.closest('[data-palette]')) openPalette(e.detail > 0);
    });
    document.addEventListener('keydown', function (e) {
      var typing = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement.tagName);
      if (((e.key === 'k' || e.key === 'K') && (e.ctrlKey || e.metaKey)) || (e.key === '/' && !typing)) {
        e.preventDefault();
        openPalette(false);
      }
    });
  }

  /* Copy a string people need verbatim */
  document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-copy]');
    if (!btn) return;
    var text = btn.getAttribute('data-copy');
    var done = function () { A.toast('Copied ' + text); };
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(done, done);
    else done();
  });

  buildShell();
  initPalette();
})();
