// Builds the Ardea brand assets into docs/design/brand/ and the explored variants into its exploration/ folder.
// usage: node docs/design/brand/build.js docs/design/brand
const fs = require('fs');
const path = require('path');
const out = process.argv[2];
fs.mkdirSync(out, { recursive: true });
const exp = path.join(out, "exploration");
fs.mkdirSync(exp, { recursive: true });

const INK = '#1D1D1F';
const ACCENT = '#0062C4';
const FONT = "-apple-system, BlinkMacSystemFont, 'SF Pro Display', 'Segoe UI Variable Display', 'Segoe UI', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif";

// Drawn on a 64-unit grid; vb is a square crop centred on the bird.
const variants = {
  a: {
    name: 'Standing',
    note: 'The whole bird at rest on its line: patient, complete, most literal.',
    vb: [1.5, 6.25, 56, 56],
    paths: [
      'M6 17 L19.5 14 C21.5 11 26 10.5 28 13.5 C29.5 16.5 27.5 19.5 25.5 22 C23.5 25 24 28.5 27.5 30.5 C33 33 40 33.5 46 37.5 L53 43.5 C47 43.5 41 44 35.5 42.5 C29 41 23.5 37.5 22 33 C21 29 21.5 25.5 23 23 C24 21 23 18.8 19.5 18.5 Z',
      'M28 13.5 L34.5 11.5',
      'M31 34.6 C36 35.3 41 36.6 45 39.2',
      'M34 42.6 V58 M39.5 43.3 V58 M28 58 H46',
    ],
    eye: [23, 15, 1],
  },
  b: {
    name: 'Watching',
    note: 'Head and neck only, the S-curve and the dagger bill: reads at every size.',
    vb: [-1.5, 6.25, 56, 56],
    paths: [
      'M6 25 L27 16.5 C29 11.5 36 10.5 39 14.5 C42 18.5 40 23.5 36.5 27.5 C33 31.5 33 36 37 40 C42 45 46 50 46 58',
      'M6 25 L28 22 C29.5 26 27.5 29.5 26 32.5 C24 37 24.5 42 28.5 46 C31.5 49 32 53 32 58',
      'M39 14.5 L47 12',
    ],
    eye: [32, 17, 1.2],
  },
  c: {
    name: 'One line',
    note: 'One continuous stroke above a ripple: the most reduced, the calmest.',
    vb: [-0.5, 6, 56, 56],
    paths: [
      'M6 16.5 L21 13 C22.5 9.5 27.5 9 29 12.5 C30.2 15.5 27.5 18 26 20.5 C24 24 25 28 29.5 30 C35 32.5 42 32 50 37 L37 37 V54',
      'M6 16.5 L20.5 17.5',
      'M27 57 C30 55 33 55 36.5 57 C40 59 43 59 46 57',
    ],
    eye: null,
  },
};

// Inner SVG markup of a mark, drawn in `color` with `sw` stroke width (grid units).
function markBody(v, color, sw) {
  const p = v.paths.map(d => `<path d="${d}"/>`).join('');
  const eye = v.eye && sw < 4 ? `<circle cx="${v.eye[0]}" cy="${v.eye[1]}" r="${v.eye[2] * sw / 2.4}" fill="${color}" stroke="none"/>` : '';
  return `<g fill="none" stroke="${color}" stroke-width="${sw}" stroke-linecap="round" stroke-linejoin="round">${p}${eye}</g>`;
}
const vbStr = v => v.vb.join(' ');

function markSvg(v, color = INK, sw = 2.4) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${vbStr(v)}" width="256" height="256" role="img" aria-label="Ardea">${markBody(v, color, sw)}</svg>\n`;
}

// Mark plus wordmark. 56 units of mark height = 64 px of logo height.
function logoSvg(v, color = INK) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 196 64" width="392" height="128" role="img" aria-label="Ardea">` +
    `<svg x="-2" y="-1" width="66" height="66" viewBox="${vbStr(v)}">${markBody(v, color, 3.1)}</svg>` +
    `<text x="68" y="44" fill="${color}" font-family="${FONT}" font-size="34" font-weight="600" letter-spacing="-0.6">Ardea</text></svg>\n`;
}

// Rounded-square app icon. `inset` is the share of the tile the bird occupies.
function iconSvg(v, sw = 2.9, inset = 0.74, size = 1024) {
  const r = size * 0.225;
  const m = size * inset;
  const o = (size - m) / 2;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}" width="${size}" height="${size}" role="img" aria-label="Ardea">` +
    `<defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1A78D6"/><stop offset="1" stop-color="${ACCENT}"/></linearGradient></defs>` +
    `<rect width="${size}" height="${size}" rx="${r}" fill="url(#g)"/>` +
    `<svg x="${o}" y="${o}" width="${m}" height="${m}" viewBox="${vbStr(v)}">${markBody(v, '#FFFFFF', sw)}</svg></svg>\n`;
}

for (const [k, v] of Object.entries(variants)) {
  fs.writeFileSync(path.join(exp, `ardea-${k}-mark.svg`), markSvg(v));
  fs.writeFileSync(path.join(exp, `ardea-${k}-logo.svg`), logoSvg(v));
  fs.writeFileSync(path.join(exp, `ardea-${k}-logo-dark.svg`), logoSvg(v, '#FFFFFF'));
  fs.writeFileSync(path.join(exp, `ardea-${k}-app-icon.svg`), iconSvg(v));
  // Favicon: same tile, heavier stroke and a larger bird so it survives 16 px.
  fs.writeFileSync(path.join(exp, `ardea-${k}-favicon.svg`), iconSvg(v, 4.6, 0.84, 64));
}

// Overview page.
const sec = Object.entries(variants).map(([k, v], i) => `
<section class="variant">
  <header><span class="tag">${String.fromCharCode(65 + i)}</span><h2>${v.name}</h2><p>${v.note}</p></header>
  <div class="grid">
    <figure class="tile big"><img src="ardea-${k}-mark.svg" alt=""><figcaption>Mark</figcaption></figure>
    <figure class="tile"><img class="logo" src="ardea-${k}-logo.svg" alt="Ardea"><figcaption>Logo, light</figcaption></figure>
    <figure class="tile dark"><img class="logo" src="ardea-${k}-logo-dark.svg" alt="Ardea"><figcaption>Logo, dark</figcaption></figure>
    <figure class="tile icons">
      <div class="row"><img src="ardea-${k}-app-icon.svg" width="120" alt=""><img src="ardea-${k}-app-icon.svg" width="60" alt=""><img src="ardea-${k}-app-icon.svg" width="40" alt=""></div>
      <figcaption>App icon, 120 · 60 · 40 px</figcaption></figure>
    <figure class="tile icons">
      <div class="row"><img src="ardea-${k}-favicon.svg" width="32" alt=""><img src="ardea-${k}-favicon.svg" width="16" alt="">
        <span class="tab"><img src="ardea-${k}-favicon.svg" width="16" alt="">Overview · Ardea</span></div>
      <figcaption>Favicon, 32 · 16 px, in a tab</figcaption></figure>
    <figure class="tile icons">
      <div class="sidebar"><span class="brand-mark"><img src="ardea-${k}-favicon.svg" alt=""></span><b>Ardea</b></div>
      <figcaption>In the app sidebar, 24 px</figcaption></figure>
  </div>
</section>`).join('');

fs.writeFileSync(path.join(exp, "variants.html"), `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Ardea logo variants</title>
<style>
:root{--canvas:#F5F5F7;--surface:#fff;--label:#1D1D1F;--secondary:#5E5E64;--border:#D7D7DC;--sidebar:#EBEBEF;color-scheme:light}
*{box-sizing:border-box}
body{margin:0;background:var(--canvas);color:var(--label);font:400 15px/1.47 ${FONT};-webkit-font-smoothing:antialiased}
main{max-width:1120px;margin:0 auto;padding:40px 16px 64px}
h1{font-size:28px;letter-spacing:-0.02em;margin:0 0 4px}
.lead{color:var(--secondary);margin:0 0 32px}
.variant{margin:0 0 40px}
.variant header{display:flex;align-items:baseline;gap:10px;flex-wrap:wrap;margin-bottom:12px}
.variant h2{font-size:17px;margin:0}
.variant p{color:var(--secondary);margin:0}
.tag{display:inline-grid;place-items:center;width:24px;height:24px;border-radius:6px;background:var(--label);color:#fff;font-weight:600;font-size:13px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:12px}
.tile{margin:0;background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:20px;display:flex;flex-direction:column;align-items:center;justify-content:flex-end;gap:16px;min-height:220px}
.tile.dark{background:#1D1D1F;border-color:#1D1D1F}
.tile.dark figcaption{color:#A1A1A6}
.tile.big img{width:140px;height:140px;margin:auto 0}
.tile img.logo{width:220px;height:auto;margin:auto 0}
figcaption{font-size:13px;color:var(--secondary)}
.row{display:flex;align-items:flex-end;gap:16px;flex-wrap:wrap;justify-content:center;margin:auto 0}
.tab{display:inline-flex;align-items:center;gap:6px;background:var(--sidebar);border-radius:8px 8px 0 0;padding:6px 12px;font-size:12px}
.sidebar{margin:auto 0;display:flex;align-items:center;gap:8px;background:var(--sidebar);border-radius:10px;padding:10px 16px;font-weight:700;letter-spacing:-0.01em}
.brand-mark img{display:block;width:24px;height:24px}
</style></head>
<body><main>
<h1>Ardea logo variants</h1>
<p class="lead">Three outlined herons, each as mark, logo with wordmark, app icon and favicon. Wordmark set in the system font; colours from the mockup tokens.</p>
${sec}
</main></body></html>
`);

// ---------- Wordmark proposals for the chosen heron (B) ----------
// Each typeface gets a heron whose line weight matches the stems of its letters.
const fonts = [
  {
    key: 'inter-tight', name: 'Inter Tight', weight: 600, sw: 3.8, iconSw: 3.0, track: '-0.025em',
    css: 'Inter+Tight:wght@600', stack: "'Inter Tight', sans-serif",
    note: 'Neutral grotesque, close to the system font of the interface. Calm and technical; the heron gets a medium line.',
  },
  {
    key: 'source-serif', name: 'Source Serif 4', weight: 500, sw: 2.3, iconSw: 2.1, track: '-0.01em',
    css: 'Source+Serif+4:opsz,wght@60,500', stack: "'Source Serif 4', serif",
    note: 'Display serif with fine contrast. Reads like a bulletin or an institute; the heron is drawn in a thin, precise line.',
  },
  {
    key: 'manrope', name: 'Manrope', weight: 800, sw: 5.0, iconSw: 4.2, track: '-0.035em',
    css: 'Manrope:wght@800', stack: "'Manrope', sans-serif",
    note: 'Geometric sans in a heavy cut. Sturdy and confident at small sizes; the heron gets a bold line to match.',
  },
];
const B = variants.b;
const inlineMark = (color, sw, cls = 'mark') =>
  `<svg class="${cls}" viewBox="${vbStr(B)}" aria-hidden="true">${markBody(B, color, sw)}</svg>`;
const inlineIcon = (sw, px) => iconSvg(B, sw, 0.74, 1024).replace('<svg ', `<svg width="${px}" height="${px}" `).replace(/ width="1024" height="1024"/, '').replace(/id="g"/, `id="g${sw}${px}"`).replace('url(#g)', `url(#g${sw}${px})`);

const props = fonts.map((f, i) => `
<section class="variant" style="--wm:${f.stack};--wmw:${f.weight};--wmt:${f.track}">
  <header><span class="tag">${i + 1}</span><h2>${f.name} <span class="meta">${f.weight} · heron line ${f.sw}</span></h2><p>${f.note}</p></header>
  <div class="grid">
    <figure class="tile wide"><div class="lockup big">${inlineMark(INK, f.sw)}<span>Ardea</span></div><figcaption>Logo, light</figcaption></figure>
    <figure class="tile dark"><div class="lockup">${inlineMark('#FFFFFF', f.sw)}<span>Ardea</span></div><figcaption>Logo, dark</figcaption></figure>
    <figure class="tile"><div class="row">${inlineIcon(f.iconSw, 120)}${inlineIcon(f.iconSw, 60)}${inlineIcon(f.iconSw, 40)}</div><figcaption>App icon, 120 · 60 · 40 px</figcaption></figure>
    <figure class="tile"><div class="sidebar"><span class="lockup small">${inlineMark(INK, f.sw * 1.15)}<span>Ardea</span></span></div><figcaption>In the app sidebar</figcaption></figure>
    <figure class="tile"><p class="specimen">Understand the risk.<br>Know what matters.<br>Act immediately.</p><figcaption>Guiding line in the same face</figcaption></figure>
  </div>
</section>`).join('');

fs.writeFileSync(path.join(exp, "wordmark.html"), `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Ardea wordmark</title>
<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?${fonts.map(f => 'family=' + f.css).join('&')}&display=block">
<style>
:root{--canvas:#F5F5F7;--surface:#fff;--label:#1D1D1F;--secondary:#5E5E64;--border:#D7D7DC;--sidebar:#EBEBEF;color-scheme:light}
*{box-sizing:border-box}
body{margin:0;background:var(--canvas);color:var(--label);font:400 15px/1.47 ${FONT};-webkit-font-smoothing:antialiased}
main{max-width:1120px;margin:0 auto;padding:40px 16px 64px}
h1{font-size:28px;letter-spacing:-0.02em;margin:0 0 4px}
.lead{color:var(--secondary);margin:0 0 32px;max-width:70ch}
.variant{margin:0 0 40px}
.variant header{display:flex;align-items:baseline;gap:10px;flex-wrap:wrap;margin-bottom:12px}
.variant h2{font-size:17px;margin:0}
.meta{font-weight:400;color:var(--secondary);font-size:13px;margin-left:4px}
.variant p{color:var(--secondary);margin:0}
.tag{display:inline-grid;place-items:center;width:24px;height:24px;border-radius:6px;background:var(--label);color:#fff;font-weight:600;font-size:13px}
.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}
.tile{margin:0;background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:20px;display:flex;flex-direction:column;align-items:center;gap:16px;min-height:200px}
.tile.wide{grid-column:span 2}
.tile.dark{background:#1D1D1F;border-color:#1D1D1F;color:#fff}
.tile.dark figcaption{color:#A1A1A6}
figcaption{font-size:13px;color:var(--secondary)}
.lockup{margin:auto 0;display:flex;align-items:center;gap:.22em;font-family:var(--wm);font-weight:var(--wmw);letter-spacing:var(--wmt);font-size:52px;line-height:1}
.lockup .mark{width:1.25em;height:1.25em;flex:none;margin-top:-.08em}
.lockup.big{font-size:84px}
.lockup.small{font-size:17px;margin:0}
.row{display:flex;align-items:flex-end;gap:16px;margin:auto 0}
.sidebar{margin:auto 0;background:var(--sidebar);border-radius:10px;padding:12px 18px;width:220px}
.variant .specimen{margin:auto 0;font-family:var(--wm);font-weight:var(--wmw);letter-spacing:var(--wmt);font-size:22px;line-height:1.2;color:var(--label);text-align:center}
@media (max-width:760px){.grid{grid-template-columns:1fr}.tile.wide{grid-column:auto}.lockup.big{font-size:56px}}
</style></head>
<body><main>
<h1>Ardea wordmark</h1>
<p class="lead">Heron B with three typefaces. Each face gets its own heron line weight so mark and letters carry the same stroke. All three fonts are open source (SIL Open Font License) and can be self-hosted.</p>
${props}
</main></body></html>
`);

// ---------- Final brand assets: heron B with Inter Tight SemiBold (owner's choice, 2026-11-18) ----------
const BRAND = { sw: 3.8, iconSw: 3.0, favSw: 4.6, font: "'Inter Tight', -apple-system, 'Segoe UI', system-ui, sans-serif", weight: 600, track: -1 };
// Lockup at font size 40: heron 50 x 50, gap 8.8, baseline 39.5.
function brandLogo(color) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 164 50" width="328" height="100" role="img" aria-label="Ardea">` +
    `<svg x="0" y="-2" width="50" height="50" viewBox="${vbStr(B)}">${markBody(B, color, BRAND.sw)}</svg>` +
    `<text x="58.8" y="39.5" fill="${color}" font-family="${BRAND.font}" font-size="40" font-weight="${BRAND.weight}" letter-spacing="${BRAND.track}">Ardea</text></svg>\n`;
}
const files = {
  'ardea-mark.svg': markSvg(B, INK, BRAND.sw),
  'ardea-mark-white.svg': markSvg(B, '#FFFFFF', BRAND.sw),
  'ardea-logo.svg': brandLogo(INK),
  'ardea-logo-dark.svg': brandLogo('#FFFFFF'),
  'ardea-app-icon.svg': iconSvg(B, BRAND.iconSw),
  'ardea-favicon.svg': iconSvg(B, BRAND.favSw, 0.84, 64),
};
for (const [name, svg] of Object.entries(files)) fs.writeFileSync(path.join(out, name), svg);

const inline = svg => svg.replace(/ width="\d+" height="\d+"/, '').trim();
fs.writeFileSync(path.join(out, 'index.html'), `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Ardea brand</title>
<link rel="icon" href="ardea-favicon.svg" type="image/svg+xml">
<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter+Tight:wght@600&display=block">
<style>
:root{--canvas:#F5F5F7;--surface:#fff;--label:#1D1D1F;--secondary:#5E5E64;--border:#D7D7DC;--sidebar:#EBEBEF;color-scheme:light}
*{box-sizing:border-box}
body{margin:0;background:var(--canvas);color:var(--label);font:400 15px/1.47 ${FONT};-webkit-font-smoothing:antialiased}
main{max-width:1120px;margin:0 auto;padding:40px 16px 64px}
h1{font-size:28px;letter-spacing:-0.02em;margin:0 0 4px}
h2{font-size:17px;margin:32px 0 12px}
.lead{color:var(--secondary);margin:0 0 24px;max-width:70ch}
.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}
.tile{margin:0;background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:24px;display:flex;flex-direction:column;align-items:center;gap:16px;min-height:220px}
.tile.wide{grid-column:span 2}
.tile.dark{background:#1D1D1F;border-color:#1D1D1F}
.tile.dark figcaption{color:#A1A1A6}
.tile.accent{background:#0062C4;border-color:#0062C4}
.tile.accent figcaption{color:#DCE9FA}
figcaption{font-size:13px;color:var(--secondary);margin-top:auto}
.logo{width:min(100%,360px);margin:auto 0 0}
.mark{width:120px;height:120px;margin:auto 0 0}
.row{display:flex;align-items:flex-end;gap:16px;margin:auto 0 0;flex-wrap:wrap;justify-content:center}
.tab{display:inline-flex;align-items:center;gap:6px;background:var(--sidebar);border-radius:8px 8px 0 0;padding:6px 12px;font-size:12px}
table{border-collapse:collapse;width:100%;background:var(--surface);border:1px solid var(--border);border-radius:12px;overflow:hidden}
th,td{text-align:left;padding:10px 16px;border-top:1px solid #E3E3E8;vertical-align:top}
tr:first-child th,tr:first-child td{border-top:0}
th{width:200px;font-weight:600}
code{font:13px ui-monospace,"SF Mono","Cascadia Mono",Consolas,monospace}
.sw{display:inline-block;width:12px;height:12px;border-radius:3px;vertical-align:-1px;margin-right:6px;border:1px solid rgba(0,0,0,.1)}
@media (max-width:760px){.grid{grid-template-columns:1fr}.tile.wide{grid-column:auto}th{width:120px}}
</style></head>
<body><main>
<h1>Ardea brand</h1>
<p class="lead">The heron, head and neck in one outlined line, with the wordmark in Inter Tight SemiBold. Chosen by the owner on 18 November 2026; the explored variants are in <a href="exploration/variants.html">exploration</a>.</p>

<div class="grid">
  <figure class="tile wide"><div class="logo">${inline(files['ardea-logo.svg'])}</div><figcaption>Logo · ardea-logo.svg</figcaption></figure>
  <figure class="tile"><div class="mark">${inline(files['ardea-mark.svg'])}</div><figcaption>Mark · ardea-mark.svg</figcaption></figure>
  <figure class="tile dark wide"><div class="logo">${inline(files['ardea-logo-dark.svg'])}</div><figcaption>Logo on dark · ardea-logo-dark.svg</figcaption></figure>
  <figure class="tile accent"><div class="mark">${inline(files['ardea-mark-white.svg'])}</div><figcaption>Mark in white · ardea-mark-white.svg</figcaption></figure>
  <figure class="tile"><div class="row"><img src="ardea-app-icon.svg" width="120" alt=""><img src="ardea-app-icon.svg" width="60" alt=""><img src="ardea-app-icon.svg" width="40" alt=""></div><figcaption>App icon · ardea-app-icon.svg</figcaption></figure>
  <figure class="tile"><div class="row"><img src="ardea-favicon.svg" width="32" alt=""><img src="ardea-favicon.svg" width="16" alt=""><span class="tab"><img src="ardea-favicon.svg" width="16" alt="">Overview · Ardea</span></div><figcaption>Favicon · ardea-favicon.svg</figcaption></figure>
  <figure class="tile"><div class="row" style="font:600 22px/1.2 ${BRAND.font};letter-spacing:-0.025em;text-align:center">Understand the risk.<br>Know what matters.<br>Act immediately.</div><figcaption>Guiding line in the wordmark face</figcaption></figure>
</div>

<h2>Rules</h2>
<table>
<tr><th>Typeface</th><td>Inter Tight SemiBold (600), tracking −0.025 em. SIL Open Font License. Only for the wordmark and display lines; the interface keeps the system font.</td></tr>
<tr><th>Heron line</th><td>On the 56-unit grid of the mark: <code>${BRAND.sw}</code> next to the wordmark and alone, <code>${BRAND.iconSw}</code> in the app icon, <code>${BRAND.favSw}</code> in the favicon (without the eye). Round caps and joins.</td></tr>
<tr><th>Lockup</th><td>Heron as tall as 1.25 × the font size, gap 0.22 em, heron raised by 0.05 em. Never set the wordmark without the heron in the logo position.</td></tr>
<tr><th>Colours</th><td><span class="sw" style="background:#1D1D1F"></span>Ink <code>#1D1D1F</code> · <span class="sw" style="background:#FFFFFF"></span>White on dark · <span class="sw" style="background:#0062C4"></span>Accent <code>#0062C4</code>, app icon from <code>#1A78D6</code> to <code>#0062C4</code></td></tr>
<tr><th>Clear space</th><td>Half the heron's height on every side.</td></tr>
<tr><th>Minimum size</th><td>Logo 96 px wide. Below 24 px use the favicon, not the mark.</td></tr>
<tr><th>Open</th><td>The wordmark in the SVG files is live text and needs Inter Tight installed or loaded; it falls back to the system font. It will be converted to outlines.</td></tr>
</table>
</main></body></html>
`);
console.log('written to', out);
