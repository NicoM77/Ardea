#!/usr/bin/env node
import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';
import { base } from '../site.config.mjs';

const dist = fileURLToPath(new URL('../dist', import.meta.url));
const port = Number(process.argv[2] ?? 4330);
const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.woff2': 'font/woff2',
  '.woff': 'font/woff',
  '.xml': 'application/xml',
  '.txt': 'text/plain; charset=utf-8',
};

createServer((req, res) => {
  const path = decodeURIComponent(new URL(req.url ?? '/', 'http://localhost').pathname);
  let file = path.startsWith(`${base}/`) ? normalize(join(dist, path.slice(base.length))) : null;
  if (file && existsSync(file) && statSync(file).isDirectory()) {
    file = join(file, 'index.html');
  }
  const found = file && file.startsWith(dist) && existsSync(file);
  const served = found ? file : join(dist, '404.html');
  res.writeHead(found ? 200 : 404, { 'content-type': types[extname(served)] ?? 'application/octet-stream' });
  createReadStream(served).pipe(res);
}).listen(port, '127.0.0.1', () => console.log(`Serving ${dist} on http://127.0.0.1:${port}${base}/`));
