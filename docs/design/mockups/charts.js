/* Ardea — planning-phase mockups.
   The charts, drawn from the dataset in data.js. Each one answers one question:
   runway  what is due when, and how much of its time is used up
   ring    how the open actions split by priority
   sieve   how many of the week's CVE records concern this organization
   line    how the number of open actions moved
   score   which facts add up to the priority
   gauge   how severe the flaw is on the CVSS scale
   Charts draw themselves once on arrival and are static after that. */
(function () {
  'use strict';

  var D = window.ARDEA, A = window.A, F = D.fmt;
  var C = window.C = {};
  var NS = 'http://www.w3.org/2000/svg';

  function css(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }
  function svg(tag, attrs, parent) {
    var n = document.createElementNS(NS, tag);
    for (var k in attrs) n.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(n);
    return n;
  }
  function onResize(fn) {
    var t, w = window.innerWidth;
    window.addEventListener('resize', function () {
      if (window.innerWidth === w) return;
      w = window.innerWidth; clearTimeout(t); t = setTimeout(fn, 120);
    });
  }

  /* ---------- Deadline runway ---------- */
  /* Fourteen days on one axis, today near the middle. A bar runs from the day an action was created to its
     deadline; the solid part is the time already used, the pale part what is left. */
  C.runway = function (el, actions) {
    var start = new Date(D.NOW); start.setHours(0, 0, 0, 0); start = new Date(start.getTime() - 6 * D.DAY);
    var DAYS = 14, span = DAYS * D.DAY, end = start.getTime() + span;
    function pos(t) { return Math.max(0, Math.min(1, (t - start) / span)) * 100; }

    var axis = '';
    for (var i = 0; i <= DAYS; i++) {
      var d = new Date(start.getTime() + i * D.DAY), x = (i / DAYS) * 100;
      if (i < DAYS && (d.getDay() === 6 || d.getDay() === 0)) axis += '<span class="weekend" style="left:' + x + '%;width:' + (100 / DAYS) + '%"></span>';
      axis += '<span class="day" style="left:' + x + '%"></span>';
    }
    for (i = 0; i < DAYS; i += 3) axis += '<span class="tick' + (i ? '' : ' first') + '" style="left:' + (i / DAYS) * 100 + '%">' + F.day(new Date(start.getTime() + i * D.DAY)) + '</span>';
    axis += '<span class="runway-now" style="left:' + pos(D.NOW) + '%"><span>Now</span></span>';

    var rows = actions.map(function (a) {
      var from = pos(a.created), to = pos(a.due), clipped = a.due > end;
      var shownEnd = Math.min(a.due, end);
      var used = Math.max(0, Math.min(1, (D.NOW - a.created) / (shownEnd - a.created))) * 100;
      var tip = 'Due ' + F.dayTime(a.due) + ' <span>· ' + D.PRIORITIES[a.priority].deadline + ' for ' + D.PRIORITIES[a.priority].label + '</span>';
      return '<a class="runway-row c-' + a.priority + '" href="' + a.href + '" data-morph data-priority="' + a.priority + '" data-id="' + a.id + '">' +
        '<span class="runway-label">' + A.sym(D.PRIORITIES[a.priority].sym) + '<span data-title>' + A.esc(a.title) + '</span><span class="sr-only">, ' + D.PRIORITIES[a.priority].label + '</span></span>' +
        A.who(a.assignee) +
        '<span class="runway-track" data-tip="' + A.esc(tip) + '">' +
          '<span class="runway-bar' + (clipped ? ' clipped' : '') + '" style="left:' + from + '%;width:' + (to - from) + '%"><i style="width:' + used + '%"></i></span>' +
          (clipped ? '' : '<span class="runway-due" style="left:' + to + '%"></span>') +
        '</span>' +
        '<span class="runway-left"><b data-left="' + a.id + '"></b><span class="sr-only"> left</span></span>' +
      '</a>';
    }).join('');

    el.innerHTML = '<div class="runway-axis" aria-hidden="true">' + axis + '</div><div class="runway-rows">' + rows + '</div><div class="runway-foot"></div>';

    A.$$('.runway-bar', el).forEach(function (bar, k) { A.grow(bar, 120 + k * 50, 700); });
    A.$$('.runway-due', el).forEach(function (tick, k) { A.fadeIn(tick, 620 + k * 50, 200); });
    A.fadeIn(A.$('.runway-now', el), 300, 400);

    /* Another part of the screen can ask for one priority to stand out */
    document.addEventListener('ardea:focus', function (e) {
      var p = e.detail;
      if (p) el.setAttribute('data-focus', p); else el.removeAttribute('data-focus');
      A.$$('.runway-row', el).forEach(function (row) { row.classList.toggle('is-focus', row.getAttribute('data-priority') === p); });
    });
  };

  /* Time left, kept current while the page is open */
  C.clock = function () {
    function tick() {
      A.$$('[data-left]').forEach(function (el) {
        var a = D.action(el.getAttribute('data-left'));
        var l = F.left(a.due);
        el.textContent = (l.over ? 'Overdue ' : '') + l.text;
      });
      A.$$('[data-countdown]').forEach(function (el) {
        var l = F.left(D.action(el.getAttribute('data-countdown')).due);
        var two = function (n) { return (n < 10 ? '0' : '') + n; };
        el.innerHTML = l.d > 0
          ? l.d + '<small>d</small>' + l.h + '<small>h</small>' + two(l.m) + '<small>m</small>'
          : l.h + '<small>h</small>' + two(l.m) + '<small>m</small>' + two(l.s) + '<small>s</small>';
      });
    }
    tick();
    setInterval(tick, 1000);
  };

  /* ---------- Priority ring ---------- */
  C.ring = function (el, parts, total) {
    var R = 50, LEN = 2 * Math.PI * R, GAP = 3, offset = 0;
    var s = svg('svg', { viewBox: '0 0 116 116', width: 116, height: 116, role: 'img', 'aria-label': total + ' open actions by priority' });
    svg('circle', { cx: 58, cy: 58, r: R, stroke: css('--rule') }, s);
    parts.forEach(function (p, k) {
      if (!p.count) return;
      var len = (p.count / total) * LEN;
      var c = svg('circle', { cx: 58, cy: 58, r: R, 'data-priority': p.priority, 'stroke-dasharray': Math.max(0, len - GAP) + ' ' + LEN, 'stroke-dashoffset': -offset }, s);
      c.style.stroke = 'var(--' + p.priority + ')';
      if (!A.reduced && c.animate) c.animate([{ strokeDasharray: '0 ' + LEN }, { strokeDasharray: Math.max(0, len - GAP) + ' ' + LEN }],
        { duration: 700, delay: 150 + k * 90, easing: A.EASE_OUT, fill: 'backwards' });
      offset += len;
    });
    el.innerHTML = '';
    el.appendChild(s);
    el.insertAdjacentHTML('beforeend', '<div class="ring-total"><b></b><span>open</span></div>');
    A.countUp(A.$('b', el), total, { duration: 700, delay: 150 });
    document.addEventListener('ardea:focus', function (e) {
      A.$$('circle[data-priority]', s).forEach(function (c) { c.style.opacity = !e.detail || c.getAttribute('data-priority') === e.detail ? 1 : 0.2; });
    });
  };

  /* ---------- The week's CVE records ---------- */
  /* One dot per published record. Three steps: every record, the ones that name a technology in a profile,
     the ones that hit an installed version. The last are links to their action. */
  C.sieve = function (root, funnel, actions) {
    var field = A.$('.sieve-field', root), canvas = A.$('canvas', field), ctx = canvas.getContext('2d');
    var N = funnel.published, seed = 20261118;
    function rnd() { seed = (seed + 0x6D2B79F5) | 0; var t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }

    var kind = new Uint8Array(N), jitter = new Float32Array(N), picked = [];
    for (var i = 0; i < N; i++) jitter[i] = rnd();
    while (picked.length < funnel.candidates) { var at = Math.floor(rnd() * N); if (!kind[at]) { kind[at] = 1; picked.push(at); } }
    var hits = [], next = 0;
    actions.forEach(function (a) { for (var k = 0; k < a.findings; k++) { kind[picked[next]] = 2; hits.push({ index: picked[next], action: a }); next++; } });

    var colour = { dot: css('--dot'), faint: css('--dot-faint'), accent: css('--accent'), pale: '#a9cbd3' };
    var geo = {}, stage = 'matched', raf = 0;

    function layout() {
      var W = field.clientWidth, targetH = W > 520 ? 168 : 190;
      var p = Math.sqrt((W * targetH) / N), cols = Math.max(1, Math.floor(W / p));
      p = W / cols;
      var rows = Math.ceil(N / cols), H = Math.ceil(rows * p), dpr = window.devicePixelRatio || 1;
      canvas.width = Math.round(W * dpr); canvas.height = Math.round(H * dpr);
      canvas.style.height = H + 'px';
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      geo = { W: W, H: H, p: p, cols: cols, r: Math.max(0.85, p * 0.3) };
    }
    function xy(index) { return [((index % geo.cols) + 0.5) * geo.p, (Math.floor(index / geo.cols) + 0.5) * geo.p]; }

    /* reveal and filter run from 0 to 1; each dot has its own moment inside them */
    function draw(reveal, filter) {
      ctx.clearRect(0, 0, geo.W, geo.H);
      var groups = { dot: [], faint: [], accent: [], pale: [] };
      for (var i = 0; i < N; i++) {
        var col = i % geo.cols;
        if (reveal < 1 && reveal < (col / geo.cols) * 0.8 + jitter[i] * 0.2) continue;
        var k = kind[i];
        if (stage === 'published') groups.dot.push(i);
        else if (!k) groups[filter >= 1 || filter > jitter[i] ? 'faint' : 'dot'].push(i);
        else if (stage === 'matched' && k === 1) groups[filter >= 1 || filter > jitter[i] * 0.6 ? 'pale' : 'dot'].push(i);
        else groups[filter >= 1 || filter > jitter[i] * 0.6 ? 'accent' : 'dot'].push(i);
      }
      Object.keys(groups).forEach(function (name) {
        var r = name === 'accent' || name === 'pale' ? geo.r * 1.35 : geo.r;
        ctx.fillStyle = colour[name];
        ctx.beginPath();
        groups[name].forEach(function (i) { var q = xy(i); ctx.moveTo(q[0] + r, q[1]); ctx.arc(q[0], q[1], r, 0, 6.2832); });
        ctx.fill();
      });
    }

    function placeHits(animate) {
      A.$$('.sieve-hit', field).forEach(function (n) { n.remove(); });
      hits.forEach(function (h, k) {
        var q = xy(h.index), a = h.action;
        var link = document.createElement('a');
        link.className = 'sieve-hit c-' + a.priority + (a.priority === 'critical' && !A.reduced ? ' ping' : '');
        link.href = a.href;
        link.style.left = q[0] + 'px'; link.style.top = q[1] + 'px';
        link.setAttribute('data-tip', '<b>' + A.esc(a.component) + '</b> <span>· ' + A.esc(a.title) + '</span>');
        link.setAttribute('aria-label', a.component + ', ' + D.PRIORITIES[a.priority].label + ': ' + a.title);
        link.innerHTML = '<i></i>';
        field.appendChild(link);
        if (animate && link.animate) link.animate([{ opacity: 0, transform: 'scale(0.4)' }, { opacity: 1, transform: 'scale(1)' }],
          { duration: 360, delay: k * 35, easing: A.EASE_OUT, fill: 'backwards' });
      });
    }

    var numbers = { published: A.$('[data-n="published"]', root), candidates: A.$('[data-n="candidates"]', root), matched: A.$('[data-n="matched"]', root), actions: A.$('[data-n="actions"]', root) };

    function settle() {
      cancelAnimationFrame(raf);
      layout(); draw(1, 1); placeHits(false);
      Object.keys(numbers).forEach(function (k) { numbers[k].textContent = F.int(funnel[k]); });
    }

    function play() {
      if (A.reduced || document.hidden) { settle(); return; }
      cancelAnimationFrame(raf);
      stage = 'matched'; press();
      layout();
      A.$$('.sieve-hit', field).forEach(function (n) { n.remove(); });
      var t0 = null, placed = false;
      A.countUp(numbers.published, funnel.published, { duration: 600 });
      A.countUp(numbers.candidates, funnel.candidates, { duration: 800, delay: 500 });
      A.countUp(numbers.matched, funnel.matched, { duration: 400, delay: 1300 });
      A.countUp(numbers.actions, funnel.actions, { duration: 400, delay: 1600 });
      (function frame(t) {
        if (t0 === null) t0 = t;
        var ms = t - t0;
        draw(Math.min(1, ms / 600), Math.max(0, Math.min(1, (ms - 500) / 900)));
        if (ms > 1300 && !placed) { placed = true; placeHits(true); }
        if (ms < 1500) raf = requestAnimationFrame(frame);
      })(performance.now());
    }

    function press() {
      field.setAttribute('data-stage', stage);
      A.$$('[data-stage-btn]', root).forEach(function (b) { b.setAttribute('aria-pressed', String(b.getAttribute('data-stage-btn') === stage)); });
    }
    A.$$('[data-stage-btn]', root).forEach(function (b) {
      b.addEventListener('click', function () { cancelAnimationFrame(raf); stage = b.getAttribute('data-stage-btn'); press(); draw(1, 1); if (!A.$('.sieve-hit', field)) placeHits(false); });
    });
    var replay = A.$('[data-replay]', root);
    if (replay) replay.addEventListener('click', play);

    Object.keys(numbers).forEach(function (k) { numbers[k].textContent = F.int(funnel[k]); });
    press(); layout(); draw(A.reduced ? 1 : 0, A.reduced ? 1 : 0);
    A.onView(root, play);
    onResize(settle);
  };

  /* ---------- Open actions over time: a stepped line ---------- */
  C.line = function (el, points) {
    var drawn = false;
    function draw(animate) {
      el.innerHTML = '';
      var w = Math.max(240, el.clientWidth), h = Math.max(150, el.clientHeight);
      var padL = 22, padR = 26, padT = 10, padB = 24;
      var max = Math.max.apply(null, points.map(function (p) { return p.open; }));
      var step = max <= 4 ? 1 : 2, top = Math.ceil((max + 1) / step) * step;
      var x = function (i) { return padL + (i * (w - padL - padR)) / (points.length - 1); };
      var y = function (v) { return padT + (h - padT - padB) * (1 - v / top); };
      var s = svg('svg', { viewBox: '0 0 ' + w + ' ' + h, width: w, height: h, role: 'img',
        'aria-label': 'Open actions from ' + F.day(points[0].date) + ' to ' + F.day(points[points.length - 1].date) + ', now ' + points[points.length - 1].open }, el);

      for (var v = 0; v <= top; v += step) {
        svg('line', { class: 'gridline', x1: padL, x2: w - padR, y1: y(v), y2: y(v) }, s);
        svg('text', { x: padL - 8, y: y(v) + 4, 'text-anchor': 'end' }, s).textContent = v;
      }
      [0, 10, 20, 30].forEach(function (i) {
        if (!points[i]) return;
        svg('text', { x: x(i), y: h - 4, 'text-anchor': i === 0 ? 'start' : i === points.length - 1 ? 'end' : 'middle' }, s).textContent = F.day(points[i].date);
      });

      var d = 'M' + x(0) + ' ' + y(points[0].open);
      for (var i = 1; i < points.length; i++) d += 'L' + x(i) + ' ' + y(points[i - 1].open) + 'L' + x(i) + ' ' + y(points[i].open);
      var last = points.length - 1;
      var area = svg('path', { class: 'area', d: d + 'L' + x(last) + ' ' + y(0) + 'L' + x(0) + ' ' + y(0) + 'Z' }, s);
      var line = svg('path', { class: 'line', d: d, pathLength: 1 }, s);
      var dot = svg('circle', { class: 'end', cx: x(last), cy: y(points[last].open), r: 4 }, s);
      var label = svg('text', { class: 'end-label', x: x(last) + 9, y: y(points[last].open) + 4 }, s);
      label.textContent = points[last].open;

      if (animate && !A.reduced && line.animate) {
        line.style.strokeDasharray = 1;
        line.animate([{ strokeDashoffset: 1 }, { strokeDashoffset: 0 }], { duration: 900, easing: A.EASE_IN_OUT, fill: 'backwards' });
        A.fadeIn(area, 500, 500); A.fadeIn(dot, 850, 200); A.fadeIn(label, 850, 200);
      }

      var rule = svg('line', { class: 'hover-rule', y1: padT, y2: h - padB }, s);
      var hover = svg('circle', { class: 'hover-dot', r: 4 }, s);
      var hit = svg('rect', { x: padL, y: 0, width: w - padL - padR, height: h - padB, fill: 'transparent' }, s);
      hit.addEventListener('pointermove', function (e) {
        var r = s.getBoundingClientRect();
        var i = Math.max(0, Math.min(last, Math.round(((e.clientX - r.left - padL) / (w - padL - padR)) * last)));
        rule.setAttribute('x1', x(i)); rule.setAttribute('x2', x(i)); rule.style.opacity = 0.4;
        hover.setAttribute('cx', x(i)); hover.setAttribute('cy', y(points[i].open)); hover.style.opacity = 1;
        A.tip.show('<b>' + points[i].open + ' open</b> <span>· ' + F.day(points[i].date) + '</span>', r.left + x(i), r.top + y(points[i].open) - 4, true);
      });
      hit.addEventListener('pointerleave', function () { rule.style.opacity = 0; hover.style.opacity = 0; A.tip.hide(); });
    }
    draw(false);
    A.onView(el, function () { drawn = true; draw(true); });
    onResize(function () { draw(false); });
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(function () { if (!drawn || A.reduced) draw(false); });
  };

  /* ---------- Priority score ---------- */
  /* One block per point. The factor that changed last sits at the end, so the earlier state is the same bar
     without it. opts.toggle wires the two dates; opts.rows lists every factor underneath. */
  C.score = function (el, vuln, opts) {
    opts = opts || {};
    var UNITS = vuln.bands[vuln.bands.length - 1].to;
    var scoring = vuln.factors.filter(function (f) { return f.points > 0; });
    scoring.sort(function (a, b) { return (a.points !== a.then) - (b.points !== b.then); });
    var view = 'now';
    function total() { return D.sum(vuln.factors, function (f) { return view === 'now' ? f.points : f.then; }); }
    function level(sum) { return vuln.bands.filter(function (b) { return sum >= b.from && sum <= b.to; })[0].priority; }

    var at = 0, segs = scoring.map(function (f) {
      var html = '<span class="score-seg" data-factor="' + A.esc(f.name) + '" style="left:' + (at / UNITS) * 100 + '%;width:' + (f.points / UNITS) * 100 + '%" data-tip="' + A.esc(f.name + ' +' + f.points) + '">' +
        (f.points > 1 ? '<span class="seg-name">' + A.esc(f.name) + '&nbsp;</span>' : '') + '+' + f.points + '</span>';
      at += f.points;
      return html;
    }).join('');
    var bands = vuln.bands.map(function (b) {
      var width = b.to - (b.from ? b.from - 1 : 0);
      return '<span class="score-band c-' + b.priority + '" data-band="' + b.priority + '" style="grid-column:span ' + width + '">' +
        A.sym(D.PRIORITIES[b.priority].sym) + '<span>' + D.PRIORITIES[b.priority].label + '</span></span>';
    }).join('');
    var rows = !opts.rows ? '' : '<div class="factors">' + vuln.factors.map(function (f) {
      return '<div class="factor" data-factor="' + A.esc(f.name) + '"><i></i><b>' + A.esc(f.name) + '</b><span></span><em></em></div>';
    }).join('') + '</div>';

    el.innerHTML = '<div class="score" style="--units:' + UNITS + '">' +
      '<div class="score-total"><b></b><span></span></div>' +
      '<div style="position:relative"><div class="score-bar" role="img">' + segs + '</div><span class="score-mark"></span></div>' +
      '<div class="score-bands" aria-hidden="true">' + bands + '</div>' + rows + '</div>';

    var root = A.$('.score', el), bar = A.$('.score-bar', el), mark = A.$('.score-mark', el), num = A.$('.score-total b', el), shown = 0;

    function render(first) {
      var sum = total(), p = level(sum);
      A.countUp(num, sum, { from: first ? 0 : shown, duration: first ? 700 : 260 });
      shown = sum;
      A.$('.score-total span', el).innerHTML = 'points · ' + A.prio(p);
      bar.setAttribute('aria-label', sum + ' points: ' + D.PRIORITIES[p].label);
      mark.style.transform = 'translateX(' + (bar.offsetWidth * sum) / UNITS + 'px)';
      A.$$('.score-seg', el).forEach(function (seg) {
        var f = vuln.factors.filter(function (x) { return x.name === seg.getAttribute('data-factor'); })[0];
        seg.classList.toggle('off', view === 'then' && f.then === 0);
      });
      A.$$('.score-band', el).forEach(function (b) { b.classList.toggle('on', b.getAttribute('data-band') === p); });
      A.$$('.factor', el).forEach(function (row, k) {
        var f = vuln.factors[k], pts = view === 'now' ? f.points : f.then;
        row.classList.toggle('zero', pts === 0);
        A.$('span', row).textContent = view === 'then' && f.evidenceThen ? f.evidenceThen : f.evidence;
        A.$('em', row).textContent = pts > 0 ? '+' + pts : '0';
        var seg = A.$('.score-seg[data-factor="' + f.name + '"]', el);
        A.$('i', row).style.background = pts > 0 && seg ? getComputedStyle(seg).backgroundColor : '';
      });
    }

    render(true);
    if (!A.reduced) {
      A.$$('.score-seg', el).forEach(function (seg, k) {
        if (seg.animate) seg.animate([{ clipPath: 'inset(0 100% 0 0)' }, { clipPath: 'inset(0 0 0 0)' }], { duration: 500, delay: 100 + k * 120, easing: A.EASE_OUT, fill: 'backwards' });
      });
      A.fadeIn(mark, 600, 200);
    }
    onResize(function () { mark.style.transition = 'none'; render(false); void mark.offsetWidth; mark.style.transition = ''; });

    /* A factor's row and its block point at each other */
    function point(name) {
      if (name) root.setAttribute('data-hover', name); else root.removeAttribute('data-hover');
      A.$$('.score-seg', el).forEach(function (seg) { seg.classList.toggle('is-hover', seg.getAttribute('data-factor') === name); });
    }
    A.$$('.factor', el).forEach(function (row) {
      var name = row.getAttribute('data-factor');
      if (!A.$('.score-seg[data-factor="' + name + '"]', el)) return;
      row.addEventListener('pointerenter', function () { point(name); });
      row.addEventListener('pointerleave', function () { point(null); });
    });

    return { set: function (v) { view = v; render(false); } };
  };

  /* ---------- Severity on an arc ---------- */
  C.gauge = function (el, value, max, label) {
    var d = 'M12 88 A72 72 0 0 1 156 88', LEN = Math.PI * 72;
    el.innerHTML = '<svg viewBox="0 0 168 96" width="168" height="96" role="img" aria-label="' + A.esc(label) + '">' +
      '<path class="bg" d="' + d + '"/><path class="fg" d="' + d + '" stroke-dasharray="' + LEN + '" stroke-dashoffset="' + (A.reduced ? LEN * (1 - value / max) : LEN) + '"/></svg>' +
      '<div class="gauge-value"><b></b></div>';
    var num = A.$('b', el);
    A.countUp(num, value, { duration: 900, format: function (v) { return v.toFixed(1); } });
    if (!A.reduced) setTimeout(function () { A.$('.fg', el).style.strokeDashoffset = LEN * (1 - value / max); }, 40);
  };
})();
