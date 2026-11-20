#!/usr/bin/env node
import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const checker = require('license-checker-rseidelsohn');
const web = fileURLToPath(new URL('..', import.meta.url));
const out = process.argv[2];
if (!out) {
  console.error('Usage: third-party-notices.mjs <output file>');
  process.exit(2);
}

checker.init({ start: web, production: true, excludePrivatePackages: true }, (error, packages) => {
  if (error) {
    console.error(error);
    process.exit(2);
  }
  const lines = ['## Web app (npm, production packages)', ''];
  for (const id of Object.keys(packages).sort()) {
    const info = packages[id];
    lines.push(`### ${id}`, '');
    lines.push(`- Licence: ${[info.licenses ?? 'UNKNOWN'].flat().join(' AND ')}`);
    if (info.repository) lines.push(`- Source: ${info.repository}`);
    lines.push('');
    const file = info.licenseFile;
    if (file && /licen[cs]e|copying/i.test(basename(file))) {
      lines.push('```text', readFileSync(file, 'utf8').trim(), '```', '');
    }
  }
  writeFileSync(out, lines.join('\n'));
  console.log(`Web notices: ${Object.keys(packages).length} packages.`);
});
