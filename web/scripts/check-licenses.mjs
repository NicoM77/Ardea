#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const checker = require('license-checker-rseidelsohn');

const web = fileURLToPath(new URL('..', import.meta.url));
const allowed = new Set(
  JSON.parse(readFileSync(join(web, '../build/licenses/allowed-licenses.json'), 'utf8')),
);
const { exceptions } = JSON.parse(readFileSync(join(web, 'license-exceptions.json'), 'utf8'));

function acceptable(expression, isAllowed) {
  const cleaned = expression.replace(/[()]/g, ' ').trim();
  return cleaned
    .split(/\s+AND\s+/i)
    .every((part) => part.split(/\s+OR\s+/i).some((id) => isAllowed(id.trim())));
}

checker.init({ start: web, production: true, excludePrivatePackages: true }, (error, packages) => {
  if (error) {
    console.error(error);
    process.exit(2);
  }
  const problems = [];
  const usedExceptions = new Set();
  for (const [id, info] of Object.entries(packages)) {
    const name = id.slice(0, id.lastIndexOf('@'));
    const licenses = [info.licenses ?? 'UNKNOWN'].flat().join(' AND ');
    if (acceptable(licenses, (l) => allowed.has(l))) continue;
    const exception = exceptions.find(
      (e) => e.package === name && acceptable(licenses, (l) => l === e.license || allowed.has(l)),
    );
    if (exception) {
      usedExceptions.add(exception);
      continue;
    }
    problems.push(`${id}: ${licenses}`);
  }
  for (const stale of exceptions.filter((e) => !usedExceptions.has(e))) {
    problems.push(
      `license-exceptions.json lists ${stale.package} (${stale.license}), which no production package needs any more`,
    );
  }
  for (const p of problems)
    console.error(`::error file=web/package-lock.json::Licence not allowed: ${p}`);
  if (problems.length) process.exit(1);
  console.log(
    `Licences: ${Object.keys(packages).length} production packages checked, ${usedExceptions.size} listed exceptions.`,
  );
});
