/* Ardea — planning-phase mockups.
   Builds the shared shell (icons, sidebar, phone bars) and the few interactions the mockups demonstrate. */
(function () {
  'use strict';

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
    more: '<circle cx="5.5" cy="12" r="1.3"/><circle cx="12" cy="12" r="1.3"/><circle cx="18.5" cy="12" r="1.3"/>',
    'chevron-right': '<path d="m9.5 5.5 6.5 6.5-6.5 6.5"/>',
    'chevron-left': '<path d="M14.5 5.5 8 12l6.5 6.5"/>',
    'chevron-updown': '<path d="m8 9 4-4 4 4M8 15l4 4 4-4"/>', /* also inlined as the .select arrow in app.css */
    'arrow-right': '<path d="M5 12h14M13 6l6 6-6 6"/>',
    search: '<circle cx="10.5" cy="10.5" r="6"/><path d="m15 15 5 5"/>',
    'check-circle': '<circle cx="12" cy="12" r="8.5"/><path d="m8.2 12.3 2.6 2.6 5-5.4"/>',
    info: '<circle cx="12" cy="12" r="8.5"/><path d="M12 11v5.2M12 7.8v.2"/>',
    external: '<path d="M14 4.5h5.5V10M19.5 4.5 11 13M17 14v4.5a1 1 0 0 1-1 1H5.5a1 1 0 0 1-1-1V8a1 1 0 0 1 1-1H10"/>',
    /* Priority symbols: shape + colour + word, never colour alone */
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
      out += '<symbol id="' + name + '" viewBox="0 0 24 24">' + ICONS[name] + '</symbol>';
    });
    return out + '</svg>';
  }

  function icon(name) {
    return '<svg class="icon" aria-hidden="true"><use href="#' + name + '"/></svg>';
  }

  function toArray(list) { return Array.prototype.slice.call(list); }

  /* Several controls exist twice (desktop and phone bar); act on the one that is shown */
  function firstVisible(elements) {
    return elements.filter(function (el) { return el.offsetParent; })[0];
  }

  /* ---------- Shell ---------- */
  var ME = { name: 'Nina Meier', initials: 'NM', role: 'Owner' };
  var NAV = [
    { id: 'overview', label: 'Overview', href: 'overview.html', icon: 'overview' },
    { id: 'actions', label: 'Actions', href: 'actions.html', icon: 'actions', count: 6 },
    { id: 'vulnerabilities', label: 'Vulnerabilities', href: '#', icon: 'vulnerabilities' },
    { id: 'projects', label: 'Projects', href: '#', icon: 'projects', count: 3 },
    { id: 'sources', label: 'Sources', href: '#', icon: 'sources' },
    { id: 'rules', label: 'Notification rules', href: '#', icon: 'rules' }
  ];
  var NAV_FOOT = [
    { id: 'team', label: 'Team', href: '#', icon: 'team' },
    { id: 'settings', label: 'Settings', href: '#', icon: 'settings' }
  ];
  /* Phone tab bar: three main sections plus More */
  var TABS = NAV.filter(function (item) { return ['overview', 'actions', 'projects'].indexOf(item.id) !== -1; })
    .concat({ id: 'more', label: 'More', href: '#', icon: 'more' });

  function navLinks(items, current, withCounts) {
    return items.map(function (item) {
      var here = item.id === current ? ' aria-current="page"' : '';
      var count = withCounts && item.count ? '<span class="nav-count">' + item.count + '<span class="sr-only"> open</span></span>' : '';
      return '<a href="' + item.href + '"' + here + '>' + icon(item.icon) + '<span>' + item.label + '</span>' + count + '</a>';
    }).join('');
  }

  function orgSwitch() {
    return '<button class="org-switch" type="button" aria-label="Organization: Example Digital AG. Switch organization">' +
      '<span class="org-avatar" aria-hidden="true">E</span>' +
      '<span class="org-name">Example Digital AG<small>3 projects</small></span>' +
      icon('chevron-updown') + '</button>';
  }

  function avatar() {
    return '<span class="user-avatar" aria-hidden="true">' + ME.initials + '</span>';
  }

  function buildShell() {
    var body = document.body;
    var section = body.getAttribute('data-section') || body.getAttribute('data-page');
    var back = document.querySelector('.content > .back');

    var sidebar =
      '<nav class="sidebar" aria-label="Main">' +
        '<div class="brand"><img src="../brand/ardea-logo.svg" alt="Ardea" width="105" height="32"></div>' +
        orgSwitch() +
        '<div class="nav">' + navLinks(NAV, section, true) + '</div>' +
        '<div class="sidebar-foot nav">' + navLinks(NAV_FOOT, section) +
          '<div class="user">' + avatar() + '<span class="user-name">' + ME.name + '<small>' + ME.role + '</small></span></div>' +
        '</div>' +
      '</nav>';

    /* Detail pages carry their back link in the content; the phone top bar shows the same link */
    var topbar = '<div class="topbar">' + (back ? back.outerHTML : orgSwitch()) +
      '<button class="account" type="button" aria-label="Account: ' + ME.name + '">' + avatar() + '</button></div>';

    body.insertAdjacentHTML('afterbegin', '<a class="skip-link" href="#main">Skip to content</a>' + sprite() + sidebar);
    var main = document.querySelector('.main');
    main.insertAdjacentHTML('afterbegin', topbar);
    main.insertAdjacentHTML('afterend', '<nav class="tabbar" aria-label="Main">' + navLinks(TABS, section) + '</nav>');
  }

  /* ---------- Actions list: segmented view, search and filters ---------- */
  function initActionList() {
    var table = document.querySelector('[data-action-table]');
    if (!table) return;

    var rows = toArray(table.querySelectorAll('tbody tr[data-state]')).map(function (row) {
      return { el: row, text: row.textContent.toLowerCase() };
    });
    var search = document.getElementById('q');
    var project = document.getElementById('project');
    var priority = document.getElementById('priority');
    var segButtons = toArray(document.querySelectorAll('[data-view]'));
    var countLine = document.getElementById('count');
    var emptyFiltered = document.getElementById('empty-filtered');
    var emptyClosed = document.getElementById('empty-closed');
    var view = 'open';
    var preset = location.hash.replace('#', '');

    /* #critical presets the priority filter, #webshop the project filter */
    [priority, project].forEach(function (select) {
      if (preset && select.querySelector('option[value="' + preset + '"]')) select.value = preset;
    });

    function apply() {
      var q = search.value.trim().toLowerCase();
      var shown = 0;
      rows.forEach(function (row) {
        var match = row.el.getAttribute('data-state') === view &&
          (!project.value || row.el.getAttribute('data-project') === project.value) &&
          (!priority.value || row.el.getAttribute('data-priority') === priority.value) &&
          (!q || row.text.indexOf(q) !== -1);
        row.el.hidden = !match;
        if (match) shown++;
      });
      var filtered = q || project.value || priority.value;
      table.hidden = shown === 0;
      emptyFiltered.hidden = !(shown === 0 && filtered);
      emptyClosed.hidden = !(shown === 0 && !filtered && view === 'closed');
      countLine.textContent = shown + (view === 'open' ? ' open ' : ' closed ') + (shown === 1 ? 'action' : 'actions') +
        (filtered ? (shown === 1 ? ' matches' : ' match') + ' your filters' : '');

      var next = priority.value || project.value;
      if (next !== preset) {
        preset = next;
        try {
          history.replaceState(null, '', next ? '#' + next : location.pathname + location.search);
        } catch (err) { /* some browsers refuse this on file:// pages; the filter still works */ }
      }
    }

    segButtons.forEach(function (btn) {
      btn.addEventListener('click', function () {
        view = btn.getAttribute('data-view');
        segButtons.forEach(function (b) { b.setAttribute('aria-pressed', String(b === btn)); });
        apply();
      });
    });
    [search, project, priority].forEach(function (el) { el.addEventListener('input', apply); });
    document.getElementById('clear').addEventListener('click', function () {
      search.value = ''; project.value = ''; priority.value = '';
      apply();
      search.focus();
    });
    apply();
  }

  /* Whole table row opens its link; the link itself stays the keyboard target */
  function initRowLinks() {
    document.addEventListener('click', function (e) {
      var row = e.target.closest('tr[data-href]');
      if (!row || e.target.closest('a, button, input, select, label')) return;
      if (window.getSelection && String(window.getSelection())) return;
      location.href = row.getAttribute('data-href');
    });
  }

  /* ---------- Action: take into review with undo, close with a reason, notes ---------- */
  /* Closing states ask for a reason; this map is also the list of closing states */
  var REASON_PROMPTS = {
    remediated: 'How was it remediated?',
    risk_accepted: 'Why is the risk accepted?',
    not_affected: 'Why is the project not affected?'
  };

  function initAction() {
    if (!document.querySelector('[data-action]')) return;

    var statusSelect = document.getElementById('status');
    var selects = toArray(document.querySelectorAll('[data-status-select]'));
    var takeButtons = toArray(document.querySelectorAll('[data-take]'));
    var assignee = document.getElementById('assignee');
    var confirmBox = document.getElementById('confirm');
    var confirmText = document.getElementById('confirm-text');
    var undo = document.getElementById('undo');
    var timeline = document.getElementById('timeline');
    var reason = document.getElementById('reason');
    var reasonText = document.getElementById('reason-text');
    var reasonLabel = document.getElementById('reason-label');
    var reasonError = document.getElementById('reason-error');
    var noteForm = document.getElementById('note-form');
    var noteText = document.getElementById('note-text');

    var current = { state: 'open', assignee: assignee.value };
    var pending = null; /* closing state waiting for its reason */
    var last = null;    /* what Undo restores */

    function label(state) {
      return statusSelect.querySelector('option[value="' + state + '"]').text;
    }

    function render() {
      selects.forEach(function (s) { s.value = current.state; });
      assignee.value = current.assignee;
      takeButtons.forEach(function (b) { b.hidden = current.state !== 'open'; });
    }

    function addActivity(text) {
      var li = document.createElement('li');
      var time = document.createElement('time');
      var p = document.createElement('p');
      time.textContent = 'Just now';
      p.textContent = text;
      li.appendChild(time);
      li.appendChild(p);
      timeline.insertBefore(li, timeline.firstChild);
      return li;
    }

    function commit(next, message, activity) {
      last = { state: current.state, assignee: current.assignee, entry: addActivity(activity) };
      current = next;
      render();
      confirmText.textContent = message;
      confirmBox.hidden = false;
      undo.focus();
    }

    function resetReason() {
      pending = null;
      reason.hidden = true;
      reasonText.value = '';
      reasonText.removeAttribute('aria-invalid');
      reasonError.hidden = true;
    }

    takeButtons.forEach(function (b) {
      b.addEventListener('click', function () {
        resetReason();
        commit({ state: 'in_review', assignee: ME.name }, 'In review and assigned to you.', ME.name + ' took the action into review.');
      });
    });

    undo.addEventListener('click', function () {
      if (!last) return;
      timeline.removeChild(last.entry);
      current = { state: last.state, assignee: last.assignee };
      last = null;
      render();
      confirmBox.hidden = true;
      (firstVisible(takeButtons) || firstVisible(selects)).focus();
    });

    selects.forEach(function (s) {
      s.addEventListener('change', function () {
        var next = s.value;
        if (next in REASON_PROMPTS) {
          pending = next;
          reasonLabel.textContent = REASON_PROMPTS[next];
          reason.hidden = false;
          selects.forEach(function (o) { o.value = next; });
          reason.scrollIntoView({ block: 'nearest' });
          reasonText.focus();
          return;
        }
        resetReason();
        commit({ state: next, assignee: current.assignee }, 'Status set to ' + label(next) + '.',
          ME.name + ' set the status to ' + label(next) + '.');
      });
    });

    assignee.addEventListener('change', function () {
      var who = assignee.value;
      commit({ state: current.state, assignee: who }, who ? 'Assigned to ' + (who === ME.name ? 'you' : who) + '.' : 'Unassigned.',
        who ? ME.name + ' assigned the action to ' + who + '.' : ME.name + ' removed the assignment.');
    });

    reason.addEventListener('submit', function (e) {
      e.preventDefault();
      var text = reasonText.value.trim();
      if (!text) {
        reasonText.setAttribute('aria-invalid', 'true');
        reasonError.hidden = false;
        reasonText.focus();
        return;
      }
      var next = pending;
      resetReason();
      commit({ state: next, assignee: current.assignee }, 'Closed as ' + label(next).toLowerCase() + '.',
        ME.name + ' closed the action as ' + label(next).toLowerCase() + ': “' + text + '”');
    });

    document.getElementById('reason-cancel').addEventListener('click', function () {
      resetReason();
      render();
      firstVisible(selects).focus();
    });

    noteForm.addEventListener('submit', function (e) {
      e.preventDefault();
      var text = noteText.value.trim();
      if (!text) { noteText.focus(); return; }
      noteText.value = '';
      commit({ state: current.state, assignee: current.assignee }, 'Note saved to the activity.', 'Note by ' + ME.name + ': ' + text);
    });

    render();
  }

  buildShell();
  initActionList();
  initRowLinks();
  initAction();
})();
