import AxeBuilder from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';
import { Secret, TOTP } from 'otpauth';

/**
 * Collects console errors and uncaught exceptions of a page; a test ends with
 * `expect(problems).toEqual([])`. The browser's own line for a 401 answer is expected: a signed-out
 * visitor gets one from GET /api/v1/auth/session.
 */
export function watchConsole(page: Page): string[] {
  const problems: string[] = [];
  page.on('console', (message) => {
    if (message.type() !== 'error') return;
    const text = message.text();
    if (/^Failed to load resource: the server responded with a status of 401\b/.test(text)) return;
    problems.push(`${page.url()} console: ${text}`);
  });
  page.on('pageerror', (error) => problems.push(`${page.url()} exception: ${error.message}`));
  return problems;
}

/**
 * The first navigation of a run. Chromium on the host network aborts a request with
 * ERR_NETWORK_CHANGED while Docker is still settling the networks it just created for the
 * instance; that is the machine, not the app, so the first page load is tried again.
 */
export async function openFirstPage(page: Page, path: string): Promise<void> {
  for (let attempt = 1; ; attempt++) {
    try {
      await page.goto(path);
      return;
    } catch (error) {
      if (attempt >= 5 || !String(error).includes('ERR_NETWORK_CHANGED')) throw error;
      await page.waitForTimeout(1000);
    }
  }
}

/** WCAG 2.2 AA with the target-size rule (master plan 11.8), on the page as it is now. */
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

/**
 * A TOTP code for the shared key, from a time step that was not used before: the server may refuse
 * a code it has already accepted, so a second code in the same 30 seconds waits for the next step.
 */
export class Authenticator {
  private readonly totp: TOTP;
  private lastStep = -1;

  constructor(sharedKey: string) {
    this.totp = new TOTP({
      secret: Secret.fromBase32(sharedKey.replace(/\s+/g, '').toUpperCase()),
      digits: 6,
      period: 30,
      algorithm: 'SHA1',
    });
  }

  async nextCode(): Promise<string> {
    let step = Math.floor(Date.now() / 30_000);
    if (step <= this.lastStep) {
      await new Promise((resolve) =>
        setTimeout(resolve, (this.lastStep + 1) * 30_000 - Date.now() + 500),
      );
      step = Math.floor(Date.now() / 30_000);
    }
    this.lastStep = step;
    return this.totp.generate();
  }
}
