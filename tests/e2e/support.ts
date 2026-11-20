import AxeBuilder from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';
import { Secret, TOTP } from 'otpauth';

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

export async function expectAccessible(page: Page, label: string): Promise<void> {
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
