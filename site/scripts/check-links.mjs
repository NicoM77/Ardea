#!/usr/bin/env node
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { base } from '../site.config.mjs';

const dist = fileURLToPath(new URL('../dist', import.meta.url));
if (!existsSync(join(dist, 'index.html'))) {
  console.error('No build in site/dist. Run npm run build first.');
  process.exit(1);
}

function pages(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    return entry.isDirectory() ? pages(path) : entry.name.endsWith('.html') ? [path] : [];
  });
}

const ids = new Map();
function idsOf(file) {
  if (!ids.has(file)) {
    const html = readFileSync(file, 'utf8');
    ids.set(file, new Set([...html.matchAll(/\s(?:id|name)="([^"]+)"/g)].map((match) => match[1])));
  }
  return ids.get(file);
}

function fileFor(pathname) {
  const path = decodeURIComponent(pathname.slice(base.length));
  const file = join(dist, path);
  if (path.endsWith('/')) {
    return join(file, 'index.html');
  }
  return existsSync(file) && statSync(file).isDirectory() ? join(file, 'index.html') : file;
}

const files = pages(dist);
const broken = [];
let checked = 0;
for (const file of files) {
  const html = readFileSync(file, 'utf8').replace(/<script\b[\s\S]*?<\/script>/g, '');
  const page = new URL(`http://site${base}/${relative(dist, file).replaceAll('\\', '/').replace(/index\.html$/, '')}`);
  const links = [
    ...[...html.matchAll(/\s(?:href|src)="([^"]*)"/g)].map((match) => match[1]),
    ...[...html.matchAll(/\ssrcset="([^"]*)"/g)].flatMap((match) => match[1].split(',').map((part) => part.trim().split(/\s+/)[0])),
  ];
  for (const raw of links) {
    const link = raw.replaceAll('&amp;', '&');
    if (link === '' || /^(?:[a-z][a-z0-9+.-]*:|\/\/)/i.test(link)) {
      continue;
    }
    checked++;
    const url = new URL(link, page);
    const where = `${relative(dist, file)}: ${link}`;
    if (url.pathname !== base && !url.pathname.startsWith(`${base}/`)) {
      broken.push(`${where} (outside ${base}/)`);
      continue;
    }
    const target = fileFor(url.pathname);
    if (!existsSync(target)) {
      broken.push(`${where} (no ${relative(dist, target)})`);
      continue;
    }
    const fragment = decodeURIComponent(url.hash.slice(1));
    if (fragment && target.endsWith('.html') && !idsOf(target).has(fragment)) {
      broken.push(`${where} (no #${fragment})`);
    }
  }
}

for (const link of broken) {
  console.error(link);
}
if (broken.length > 0) {
  console.error(`${broken.length} broken internal links.`);
  process.exit(1);
}
console.log(`Links: ${checked} internal links in ${files.length} pages, none broken.`);
