import { Component, ViewEncapsulation } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

/**
 * Frame of the component library pages. Development only and never shipped, so its own labels
 * are plain English rather than translation keys (scripts/check-translations.mjs skips this folder).
 * Encapsulation is off: the `.sc-*` classes lay out every page below.
 */
@Component({
  selector: 'ardea-showcase-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  encapsulation: ViewEncapsulation.None,
  template: `
    <nav class="sc-nav" aria-label="Component library">
      <a routerLink="/ui" class="sc-brand">
        <img src="brand/ardea-logo.svg" alt="Ardea" width="85" height="26" />
        <span>Component library</span>
      </a>
      <ul>
        @for (page of pages; track page.path) {
          <li>
            <a [routerLink]="page.path" routerLinkActive="is-current" ariaCurrentWhenActive="page">
              {{ page.label }}
            </a>
          </li>
        }
      </ul>
    </nav>
    <main class="sc-sheet" id="main">
      <router-outlet />
    </main>
  `,
  styles: `
    ardea-showcase-layout {
      display: grid;
      grid-template-columns: 200px minmax(0, 1fr);
      min-height: 100vh;
    }
    .sc-nav {
      padding: var(--space-md);
    }
    .sc-brand {
      display: grid;
      gap: var(--space-xs);
      padding: var(--space-sm);
      margin-bottom: var(--space-md);
      color: var(--muted);
      font-size: var(--fs-small);
    }
    .sc-brand img {
      height: 26px;
      width: auto;
    }
    .sc-nav ul {
      display: grid;
      gap: 2px;
    }
    .sc-nav li a {
      display: flex;
      align-items: center;
      height: 32px;
      padding: 0 var(--space-sm);
      border-radius: var(--radius-card);
      color: var(--muted);
    }
    .sc-nav li a.is-current {
      background: var(--active);
      color: var(--text);
      font-weight: 500;
    }
    .sc-sheet {
      min-width: 0;
      margin: var(--space-sm) var(--space-sm) var(--space-sm) 0;
      padding: var(--pad) var(--pad) var(--space-2xl);
      border: 1px solid var(--border);
      border-radius: var(--radius-sheet);
      background: var(--surface);
    }
    .sc-page {
      display: grid;
      gap: var(--gap);
      max-width: var(--content-max);
    }
    .sc-intro {
      margin-top: calc(-1 * var(--space-md));
      color: var(--muted);
      max-width: 72ch;
    }
    .sc-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
      gap: var(--gap);
    }
    .sc-rows {
      display: grid;
    }
    .sc-row {
      display: grid;
      grid-template-columns: 120px minmax(0, 1fr);
      gap: var(--space-lg);
      align-items: center;
      min-height: 48px;
      padding: var(--space-sm) 0;
    }
    .sc-row + .sc-row {
      border-top: 1px solid var(--rule);
    }
    .sc-label {
      color: var(--muted);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .sc-demo {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-md) var(--space-xl);
      min-width: 0;
    }
    .sc-stack {
      display: grid;
      gap: var(--space-lg);
      max-width: 400px;
    }
    .sc-swatches {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(132px, 1fr));
      gap: var(--space-lg);
    }
    .sc-swatch i {
      display: block;
      height: 48px;
      margin-bottom: var(--space-sm);
      border: 1px solid var(--border);
      border-radius: var(--radius-control);
    }
    .sc-swatch b {
      display: block;
      font-weight: 500;
    }
    .sc-swatch span {
      display: block;
      color: var(--muted);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .sc-frame {
      overflow: hidden;
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--ground);
    }
  `,
})
export class ShowcaseLayout {
  protected readonly pages = [
    { path: 'foundations', label: 'Foundations' },
    { path: 'buttons', label: 'Buttons' },
    { path: 'fields', label: 'Fields' },
    { path: 'feedback', label: 'Feedback' },
    { path: 'signals', label: 'Priority and status' },
    { path: 'two-factor', label: 'Two-factor' },
    { path: 'shell', label: 'Shell' },
  ];
}
