import AxeBuilder from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';

/**
 * WCAG 2.2 AA with the target-size rule (master plan 11.8) on the page as it is now; the same rule
 * set as the end-to-end tests against the real instance (tests/e2e/support.ts).
 */
export async function expectAccessible(page: Page, label: string): Promise<void> {
  // Arrival animations fade content in; contrast is judged once they have finished (loops excepted).
  await page.waitForFunction(() =>
    document
      .getAnimations()
      .every((a) => a.playState !== 'running' || a.effect?.getTiming().iterations === Infinity),
  );
  const result = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  const violations = result.violations.map(
    (v) =>
      `${label}: ${v.id} (${v.impact}) ${v.help} – ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`,
  );
  expect(violations).toEqual([]);
}
