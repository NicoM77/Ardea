#!/usr/bin/env node
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('..', import.meta.url));
const allowed = new Set([join(root, 'projects/ui/src/styles/tokens.css')]);
const colour =
  /#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?|oklch|oklab|lab|lch|hwb)\(|\b(?:white|black)\b(?=\s*[;,)}])/g;

function* files(dir) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) yield* files(path);
    else if (/\.(css|ts|html)$/.test(name) && !/\.spec\.ts$/.test(name)) yield path;
  }
}

function styleParts(path, text) {
  if (path.endsWith('.css')) return [{ text, offset: 0 }];
  const parts = [];
  for (const m of text.matchAll(/styles:\s*`([\s\S]*?)`/g))
    parts.push({ text: m[1], offset: m.index + m[0].indexOf('`') + 1 });
  for (const m of text.matchAll(/style="([^"]*)"/g))
    parts.push({ text: m[1], offset: m.index + 7 });
  return parts;
}

const problems = [];
for (const dir of ['projects/ardea/src', 'projects/ui/src'].map((p) => join(root, p))) {
  for (const path of files(dir)) {
    if (allowed.has(path)) continue;
    const text = readFileSync(path, 'utf8');
    for (const part of styleParts(path, text)) {
      const code = part.text.replace(/\/\*[\s\S]*?\*\//g, (c) => c.replace(/[^\n]/g, ' '));
      for (const m of code.matchAll(colour)) {
        const line = text.slice(0, part.offset + m.index).split('\n').length;
        problems.push({ file: relative(root, path), line, value: m[0] });
      }
    }
  }
}

for (const p of problems) {
  console.error(
    `::error file=web/${p.file},line=${p.line}::Colour value "${p.value}" outside the token file; use a token from projects/ui/src/styles/tokens.css`,
  );
}
if (problems.length) process.exit(1);
console.log('Tokens: no colour values outside the token file.');
