#!/usr/bin/env node
/*
 * Serves a built app for the Playwright tests, with the single-page fallback to index.html and the
 * same Content Security Policy the API sends in production. The policy is read from
 * src/Hosts/Ardea.Host/Http/RequestProtection.cs, so a test here fails exactly where the real
 * instance would (no 'unsafe-eval', no inline script). Plain Node: it runs on the Node of the
 * Playwright image, which is older than the Angular CLI needs.
 *
 * Usage: node scripts/serve-static.mjs <dir> <port>
 */
import { createReadStream, existsSync, readFileSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(process.argv[2] ?? 'dist/ardea/browser');
const port = Number(process.argv[3] ?? 4300);
const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.webmanifest': 'application/manifest+json',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
  '.woff': 'font/woff',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
  '.txt': 'text/plain; charset=utf-8',
};

/** The policy as the host sets it: the string literals of `ContentSecurityPolicy = "..." + "...";`. */
function productionPolicy() {
  const source = fileURLToPath(
    new URL('../../src/Hosts/Ardea.Host/Http/RequestProtection.cs', import.meta.url),
  );
  const code = readFileSync(source, 'utf8');
  const statement = /ContentSecurityPolicy\s*=([\s\S]*?);\s*$/m.exec(code)?.[1];
  const parts = statement ? [...statement.matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((m) => m[1]) : [];
  const policy = parts.join('');
  if (!policy.includes('script-src')) {
    throw new Error(`No Content-Security-Policy found in ${source}`);
  }
  return policy;
}

if (!existsSync(join(root, 'index.html'))) {
  console.error(`No build in ${root}.`);
  process.exit(1);
}

const policy = productionPolicy();

createServer((req, res) => {
  const path = decodeURIComponent(new URL(req.url ?? '/', 'http://localhost').pathname);
  let file = normalize(join(root, path));
  if (!file.startsWith(root) || !existsSync(file) || statSync(file).isDirectory()) {
    file = join(root, 'index.html');
  }
  res.writeHead(200, {
    'content-type': types[extname(file)] ?? 'application/octet-stream',
    'content-security-policy': policy,
  });
  createReadStream(file).pipe(res);
}).listen(port, '127.0.0.1', () => console.log(`Serving ${root} on http://127.0.0.1:${port}`));
