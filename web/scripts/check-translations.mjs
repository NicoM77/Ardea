#!/usr/bin/env node
/*
 * Fails when a translation key used in the code is missing from en.json, or when en.json holds a
 * key nothing uses (master plan 2.4: keys are checked for completeness in the build).
 *
 * A key is any quoted string of dotted camelCase words whose first word is a top-level group of
 * en.json ("auth.signIn.title"), plus any quoted string piped into `transloco`. Keys are always
 * written out in full; no key is built from parts at runtime, so this search finds every one.
 *
 * Skipped: specs, and the development-only component library pages (src/app/showcase), which are
 * never shipped and keep their labels in plain English.
 */
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('..', import.meta.url));
const enPath = join(root, 'projects/ardea/src/i18n/en.json');
const sourceRoots = ['projects/ardea/src', 'projects/ui/src'].map((p) => join(root, p));
const skip = [/\.spec\.ts$/, /[\\/]showcase[\\/]/, /[\\/]api[\\/]generated[\\/]/];

function flatten(node, prefix = '', out = new Map()) {
  for (const [key, value] of Object.entries(node)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value && typeof value === 'object') flatten(value, path, out);
    else out.set(path, value);
  }
  return out;
}

function* files(dir) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) yield* files(path);
    else if (/\.(ts|html)$/.test(name) && !skip.some((re) => re.test(path))) yield path;
  }
}

const en = flatten(JSON.parse(readFileSync(enPath, 'utf8')));
const groups = new Set([...en.keys()].map((k) => k.split('.')[0]));

const literal = /['"`]([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)['"`]/g;
const piped = /['"]([^'"\s]+)['"]\s*\)?\s*\|\s*transloco\b/g;

const used = new Map(); // key -> first file
for (const dir of sourceRoots) {
  for (const file of files(dir)) {
    const text = readFileSync(file, 'utf8');
    for (const match of text.matchAll(literal)) {
      const key = match[1];
      if (groups.has(key.split('.')[0]) && !used.has(key)) used.set(key, relative(root, file));
    }
    for (const match of text.matchAll(piped)) {
      if (!used.has(match[1])) used.set(match[1], relative(root, file));
    }
  }
}

const missing = [...used].filter(([key]) => !en.has(key));
const unused = [...en.keys()].filter((key) => !used.has(key));
const empty = [...en].filter(([, value]) => typeof value !== 'string' || value.trim() === '');

for (const [key, file] of missing)
  console.error(`::error file=web/${file}::Missing in en.json: ${key}`);
for (const key of unused)
  console.error(`::error file=web/projects/ardea/src/i18n/en.json::Not used anywhere: ${key}`);
for (const [key] of empty)
  console.error(`::error file=web/projects/ardea/src/i18n/en.json::Empty text: ${key}`);

if (missing.length || unused.length || empty.length) {
  console.error(
    `Translations: ${missing.length} missing, ${unused.length} unused, ${empty.length} empty.`,
  );
  process.exit(1);
}
console.log(`Translations: ${en.size} keys in en.json, all used and all present.`);
