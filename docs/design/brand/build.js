// Builds the Ardea brand assets and the brand sheet into docs/design/brand/.
// usage: node docs/design/brand/build.js docs/design/brand
const fs = require('fs');
const path = require('path');
const out = process.argv[2];
fs.mkdirSync(out, { recursive: true });

const INK = '#1D1D1F';
const ACCENT = '#0062C4';
const FONT = "-apple-system, BlinkMacSystemFont, 'SF Pro Display', 'Segoe UI Variable Display', 'Segoe UI', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif";

// The heron: head and neck in one outlined line, drawn on a 64-unit grid.
// vb is the square crop (56 units) centred on the bird.
const HERON = {
  vb: [-1.5, 6.25, 56, 56],
  paths: [
    'M6 25 L27 16.5 C29 11.5 36 10.5 39 14.5 C42 18.5 40 23.5 36.5 27.5 C33 31.5 33 36 37 40 C42 45 46 50 46 58',
    'M6 25 L28 22 C29.5 26 27.5 29.5 26 32.5 C24 37 24.5 42 28.5 46 C31.5 49 32 53 32 58',
    'M39 14.5 L47 12',
  ],
  eye: [32, 17, 1.2],
};

// Line weights on the grid, wordmark settings. Chosen by the owner on 2026-11-18.
const BRAND = {
  sw: 3.8,       // mark alone and next to the wordmark
  iconSw: 3.0,   // app icon
  favSw: 4.6,    // favicon, drawn without the eye
  font: "'Inter Tight', -apple-system, 'Segoe UI', system-ui, sans-serif",
  weight: 600,
  track: -1,     // -0.025 em at 40
};

const vb = HERON.vb.join(' ');

// Inner markup of the heron in `color` with line weight `sw`. The eye is left out from 4 units up.
function heron(color, sw) {
  const p = HERON.paths.map(d => `<path d="${d}"/>`).join('');
  const [cx, cy, r] = HERON.eye;
  const eye = sw < 4 ? `<circle cx="${cx}" cy="${cy}" r="${r * sw / 2.4}" fill="${color}" stroke="none"/>` : '';
  return `<g fill="none" stroke="${color}" stroke-width="${sw}" stroke-linecap="round" stroke-linejoin="round">${p}${eye}</g>`;
}

function mark(color) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${vb}" width="256" height="256" role="img" aria-label="Ardea">${heron(color, BRAND.sw)}</svg>\n`;
}

// Lockup at font size 40: heron 50 x 50, gap 8.8, baseline 39.5.
function logo(color) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 164 50" width="328" height="100" role="img" aria-label="Ardea">` +
    `<svg x="0" y="-2" width="50" height="50" viewBox="${vb}">${heron(color, BRAND.sw)}</svg>` +
    `<text x="58.8" y="39.5" fill="${color}" font-family="${BRAND.font}" font-size="40" font-weight="${BRAND.weight}" letter-spacing="${BRAND.track}">Ardea</text></svg>\n`;
}

// Rounded-square tile with the heron in white. `inset` is the share of the tile the heron occupies.
function icon(sw, inset, size) {
  const r = size * 0.225;
  const m = size * inset;
  const o = (size - m) / 2;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}" width="${size}" height="${size}" role="img" aria-label="Ardea">` +
    `<defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1A78D6"/><stop offset="1" stop-color="${ACCENT}"/></linearGradient></defs>` +
    `<rect width="${size}" height="${size}" rx="${r}" fill="url(#g)"/>` +
    `<svg x="${o}" y="${o}" width="${m}" height="${m}" viewBox="${vb}">${heron('#FFFFFF', sw)}</svg></svg>\n`;
}

const files = {
  'ardea-mark.svg': mark(INK),
  'ardea-mark-white.svg': mark('#FFFFFF'),
  'ardea-logo.svg': logo(INK),
  'ardea-logo-dark.svg': logo('#FFFFFF'),
  'ardea-app-icon.svg': icon(BRAND.iconSw, 0.74, 1024),
  'ardea-favicon.svg': icon(BRAND.favSw, 0.84, 64),
};
for (const [name, svg] of Object.entries(files)) fs.writeFileSync(path.join(out, name), svg);

// Brand sheet. Logos are inlined so the page's web font sets the wordmark.
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
<p class="lead">The heron, head and neck in one outlined line, with the wordmark in Inter Tight SemiBold. Chosen by the owner on 18 November 2026.</p>

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
