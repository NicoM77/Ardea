import type { Page } from '@playwright/test';

export function watchConsole(page: Page): string[] {
  const problems: string[] = [];
  page.on('console', (message) => {
    if (message.type() !== 'error') return;
    const text = message.text();
    if (/^Failed to load resource: the server responded with a status of 401\b/.test(text)) return;
    problems.push(`console: ${text}`);
  });
  page.on('pageerror', (error) => problems.push(`exception: ${error.message}`));
  void page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (event) =>
      console.error(`CSP violation: ${event.violatedDirective} (${event.blockedURI})`),
    );
  });
  return problems;
}
