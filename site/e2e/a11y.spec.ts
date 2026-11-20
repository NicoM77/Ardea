import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { readdirSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { base } from '../site.config.mjs';

const dist = fileURLToPath(new URL('../dist', import.meta.url));
const sampledOperations = ['login', 'listnotices', 'answernotice'];

function pages(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    return entry.isDirectory() ? pages(path) : entry.name === 'index.html' ? [relative(dist, dir).replaceAll('\\', '/')] : [];
  });
}

const operation = /^api\/operations\/([^/]+)$/;
const paths = pages(dist)
  .filter((path) => !operation.test(path) || sampledOperations.includes(operation.exec(path)![1]))
  .sort();

async function expectAccessible(page: Page, label: string): Promise<void> {
  await page.waitForFunction(() =>
    [...document.querySelectorAll('.expressive-code pre')].every((pre) => pre.scrollWidth <= pre.clientWidth || pre.hasAttribute('tabindex')),
  );
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
  const violations = result.violations.map(
    (v) =>
      `${label}: ${v.id} (${v.impact}) ${v.help} – ${v.nodes.map((n) => `${n.target.join(' ')}: ${n.any[0]?.message ?? ''}`).join(', ')}`,
  );
  expect(violations).toEqual([]);
}

test('every sampled operation page exists', () => {
  expect(paths.filter((path) => operation.test(path))).toHaveLength(sampledOperations.length);
});

for (const path of paths) {
  test(`axe: /${path}`, async ({ page }) => {
    await page.goto(`${base}/${path ? `${path}/` : ''}`);
    await expectAccessible(page, `/${path}`);
  });
}

test('axe: landing page at phone width', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`${base}/`);
  await expectAccessible(page, '/ (phone)');
});
