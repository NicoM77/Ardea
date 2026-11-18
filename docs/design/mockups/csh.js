/* CSH Alert mockups: just enough behaviour to try the two promised interactions.
   Planning-phase prototype code, not the production frontend. */
(function () {
  'use strict';

  /* Alerts: the danger ladder filters the list to one level. */
  var table = document.getElementById('alerts');
  if (table) {
    var steps = [].slice.call(document.querySelectorAll('.step[data-level]'));
    var rows = [].slice.call(table.querySelectorAll('tbody tr'));
    var count = document.getElementById('count');
    var reset = document.getElementById('ladder-reset');
    var note = document.getElementById('l1-note');
    var names = { 5: 'Critical', 4: 'High', 3: 'Medium', 2: 'Low', 1: 'Not relevant' };

    var apply = function (level) {
      var shown = 0;
      rows.forEach(function (row) {
        var rowLevel = row.getAttribute('data-level');
        var show = level ? rowLevel === level : rowLevel !== '1';
        row.hidden = !show;
        if (show) { shown += 1; }
      });
      steps.forEach(function (step) {
        step.setAttribute('aria-pressed', String(step.getAttribute('data-level') === level));
      });
      reset.hidden = !level;
      note.hidden = level !== '1';
      count.textContent = level
        ? shown + (shown === 1 ? ' alert' : ' alerts') + ' at level ' + level + ', ' + names[level]
        : shown + ' alerts at levels 2 to 5';
      if (window.history && history.replaceState) {
        history.replaceState(null, '', level ? '#level-' + level : location.pathname + location.search);
      }
    };

    steps.forEach(function (step) {
      step.addEventListener('click', function () {
        var pressed = step.getAttribute('aria-pressed') === 'true';
        apply(pressed ? null : step.getAttribute('data-level'));
      });
    });
    reset.addEventListener('click', function () { apply(null); });

    var fromHash = /^#level-([1-5])$/.exec(location.hash);
    if (fromHash) { apply(fromHash[1]); }
  }

  /* Alert detail: the working state changes in place, with an undo.
     Desktop shows the status select in the head, phones in the action bar. */
  var selects = [].slice.call(document.querySelectorAll('[data-status]'));
  if (selects.length) {
    var buttons = [].slice.call(document.querySelectorAll('[data-advance]'));
    var labels = [].slice.call(document.querySelectorAll('[data-status-text]'));
    var icons = [].slice.call(document.querySelectorAll('[data-status-icon]'));
    var iconFor = { 'Open': '#i-open', 'In review': '#i-review', 'Remediated': '#i-done', 'Risk accepted': '#i-flag', 'Not affected': '#i-none' };
    var confirmNote = document.getElementById('status-note');
    var confirmText = document.getElementById('status-note-text');
    var undo = document.getElementById('status-undo');
    var next = { 'Open': 'In review', 'In review': 'Remediated' };
    var verb = { 'Open': 'Take into review', 'In review': 'Mark remediated' };
    var current = selects[0].value;
    var previous = current;

    var render = function () {
      selects.forEach(function (select) { select.value = current; });
      labels.forEach(function (label) { label.textContent = current; });
      icons.forEach(function (icon) { icon.setAttribute('href', iconFor[current]); });
      buttons.forEach(function (button) {
        var label = verb[current];
        button.textContent = label || current;
        button.disabled = !label;
        button.removeAttribute('aria-busy');
      });
    };
    var commit = function (value) {
      previous = current;
      current = value;
      render();
      confirmText.textContent = 'Status set to ' + value + (value === 'In review' ? '. You are assigned.' : '.');
      confirmNote.hidden = false;
    };

    buttons.forEach(function (button) {
      button.addEventListener('click', function () {
        var target = next[current];
        if (!target) { return; }
        buttons.forEach(function (b) { b.setAttribute('aria-busy', 'true'); b.disabled = true; b.textContent = 'Saving…'; });
        window.setTimeout(function () { commit(target); }, 450);
      });
    });
    selects.forEach(function (select) {
      select.addEventListener('change', function () { commit(select.value); });
    });
    undo.addEventListener('click', function () {
      current = previous;
      render();
      confirmNote.hidden = true;
      var visible = selects.filter(function (select) { return select.offsetParent !== null; })[0];
      if (visible) { visible.focus(); }
    });
    render();
  }
})();
