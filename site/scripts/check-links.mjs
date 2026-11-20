#!/usr/bin/env node
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ELEMENT_NODE, parse, walkSync } from 'ultrahtml';
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

const rawText = new Set(['script', 'style', 'template', 'noscript']);
function insideRawText(node) {
  for (let parent = node.parent; parent; parent = parent.parent) {
    if (parent.type === ELEMENT_NODE && rawText.has(parent.name.toLowerCase())) {
      return true;
    }
  }
  return false;
}

const parsed = new Map();
function read(file) {
  if (!parsed.has(file)) {
    const ids = new Set();
    const links = [];
    walkSync(parse(readFileSync(file, 'utf8')), (node) => {
      if (node.type !== ELEMENT_NODE || insideRawText(node)) {
        return;
      }
      const attributes = node.attributes ?? {};
      for (const key of ['id', 'name']) {
        if (attributes[key]) {
          ids.add(attributes[key]);
        }
      }
      for (const key of ['href', 'src']) {
        if (key in attributes) {
          links.push(attributes[key]);
        }
      }
      if (attributes.srcset) {
        links.push(...attributes.srcset.split(',').map((part) => part.trim().split(/\s+/)[0]));
      }
    });
    parsed.set(file, { ids, links });
  }
  return parsed.get(file);
}

function idsOf(file) {
  return read(file).ids;
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
  const page = new URL(`http://site${base}/${relative(dist, file).replaceAll('\\', '/').replace(/index\.html$/, '')}`);
  const { links } = read(file);
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
