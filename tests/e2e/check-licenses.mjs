#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const checker = require('license-checker-rseidelsohn');

const here = fileURLToPath(new URL('.', import.meta.url));
const allowed = new Set(
  JSON.parse(readFileSync(join(here, '../../build/licenses/allowed-licenses.json'), 'utf8')),
);

function acceptable(expression) {
  return expression
    .replace(/[()]/g, ' ')
    .trim()
    .split(/\s+AND\s+/i)
    .every((part) => part.split(/\s+OR\s+/i).some((id) => allowed.has(id.trim())));
}

checker.init({ start: here, production: true, excludePrivatePackages: true }, (error, packages) => {
  if (error) {
    console.error(error);
    process.exit(2);
  }
  const problems = Object.entries(packages)
    .map(([id, info]) => [id, [info.licenses ?? 'UNKNOWN'].flat().join(' AND ')])
    .filter(([, licenses]) => !acceptable(licenses));
  for (const [id, licenses] of problems) {
    console.error(
      `::error file=tests/e2e/package-lock.json::Licence not allowed: ${id}: ${licenses}`,
    );
  }
  if (problems.length) process.exit(1);
  console.log(`Licences: ${Object.keys(packages).length} test packages checked, all allowed.`);
});
