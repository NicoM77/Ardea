#!/usr/bin/env node
import { copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, posix } from 'node:path';
import { fileURLToPath } from 'node:url';
import { base, repository } from '../site.config.mjs';

const site = fileURLToPath(new URL('..', import.meta.url));
const root = join(site, '..');
const content = join(site, 'src', 'content', 'docs');
const media = join(site, 'public', 'media');

const adrs = readdirSync(join(root, 'docs', 'adr'))
  .filter((name) => /^\d{4}-.+\.md$/.test(name))
  .sort()
  .map((name) => ({ source: `docs/adr/${name}`, slug: `adr/${name.replace(/\.md$/, '')}` }));

const pages = [
  { source: 'docs/operations/README.md', slug: 'operations' },
  { source: 'docs/operations/try-it.md', slug: 'operations/try-it' },
  { source: 'docs/operations/installation.md', slug: 'operations/installation' },
  { source: 'docs/operations/configuration.md', slug: 'operations/configuration' },
  { source: 'docs/operations/operations.md', slug: 'operations/operation' },
  { source: 'docs/operations/update.md', slug: 'operations/update' },
  { source: 'docs/operations/add-a-source.md', slug: 'operations/add-a-source' },
  { source: 'docs/architecture.md', slug: 'architecture' },
  { source: 'docs/architecture/data-model.md', slug: 'architecture/data-model' },
  { source: 'docs/architecture/rule-set.md', slug: 'architecture/rule-set' },
  { source: 'docs/adr/README.md', slug: 'adr' },
  ...adrs,
  { source: 'SECURITY.md', slug: 'security' },
  { source: 'docs/security/security-model.md', slug: 'security/security-model' },
  { source: 'docs/security/threat-review.md', slug: 'security/threat-review' },
  { source: 'CONTRIBUTING.md', slug: 'contributing' },
];

const assets = [
  { source: 'docs/design/brand/ardea-logo.svg', target: 'ardea-logo.svg' },
  { source: 'docs/design/brand/ardea-favicon.svg', target: 'ardea-favicon.svg' },
  ...readdirSync(join(root, 'docs', 'screenshots'))
    .filter((name) => name.endsWith('.png'))
    .map((name) => ({ source: `docs/screenshots/${name}`, target: `screenshots/${name}` })),
];

const bySource = new Map(pages.map((page) => [page.source, page]));
const problems = [];

function target(from, link) {
  if (/^[a-z][a-z0-9+.-]*:/i.test(link) || link.startsWith('#') || link.startsWith('/')) {
    return link;
  }
  const hashAt = link.indexOf('#');
  const path = hashAt < 0 ? link : link.slice(0, hashAt);
  const hash = hashAt < 0 ? '' : link.slice(hashAt);
  const resolved = posix.normalize(posix.join(posix.dirname(from), decodeURI(path))).replace(/\/$/, '');
  if (resolved.startsWith('..')) {
    problems.push(`${from}: ${link} points outside the repository`);
    return link;
  }
  const page = bySource.get(resolved);
  if (page) {
    return `${base}/${page.slug}/${hash}`;
  }
  const file = join(root, resolved);
  if (!existsSync(file)) {
    problems.push(`${from}: ${link} does not exist (${resolved})`);
    return link;
  }
  const kind = statSync(file).isDirectory() ? 'tree' : 'blob';
  return `${repository}/${kind}/main/${resolved}${hash}`;
}

function rewrite(from, text) {
  let fence = null;
  return text
    .split('\n')
    .map((line) => {
      const opening = /^\s*(`{3,}|~{3,})/.exec(line);
      if (fence) {
        if (opening && opening[1][0] === fence[0] && opening[1].length >= fence.length) {
          fence = null;
        }
        return line;
      }
      if (opening) {
        fence = opening[1];
        return line;
      }
      return line
        .split(/(`+[^`]*`+)/)
        .map((part, index) =>
          index % 2 === 1 ? part : part.replace(/\]\(([^)\s]+)((?:\s+"[^"]*")?)\)/g, (_, link, title) => `](${target(from, link)}${title})`),
        )
        .join('');
    })
    .join('\n');
}

function page({ source, slug }) {
  const text = readFileSync(join(root, source), 'utf8').replace(/\r\n/g, '\n');
  const heading = /^# (.+)\n+/m.exec(text);
  if (!heading || text.slice(0, heading.index).trim() !== '') {
    problems.push(`${source}: does not start with a level-one heading`);
    return;
  }
  const title = heading[1].trim();
  const body = rewrite(source, text.slice(heading.index + heading[0].length));
  const frontmatter = ['---', `title: ${JSON.stringify(title)}`, `editUrl: ${JSON.stringify(`${repository}/edit/main/${source}`)}`, '---', ''];
  const file = join(content, `${slug}.md`);
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, frontmatter.join('\n') + body);
}

rmSync(content, { recursive: true, force: true });
rmSync(media, { recursive: true, force: true });
pages.forEach(page);
for (const asset of assets) {
  const file = join(media, asset.target);
  mkdirSync(dirname(file), { recursive: true });
  copyFileSync(join(root, asset.source), file);
}

if (problems.length > 0) {
  for (const problem of problems) {
    console.error(problem);
  }
  process.exit(1);
}
console.log(`Docs: ${pages.length} pages and ${assets.length} images from docs/ into the site.`);
