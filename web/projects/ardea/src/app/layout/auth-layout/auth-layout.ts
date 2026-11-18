import { Component, ViewEncapsulation } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * The frame for everything before the shell: setup, sign-in, two-factor, registration and the
 * password pages. The ground, the logo, and one white sheet with the form; nothing else on the page.
 *
 * Encapsulation is off so the pages inside share one set of rules for their title, lead sentence,
 * form rhythm and footer links (`.auth-*` classes), instead of repeating them in every page.
 */
@Component({
  selector: 'ardea-auth-layout',
  imports: [RouterOutlet, TranslocoPipe],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="auth-frame">
      <header class="auth-brand">
        <img src="brand/ardea-logo.svg" [alt]="'app.name' | transloco" width="98" height="30" />
      </header>
      <main class="auth-sheet" id="main">
        <router-outlet />
      </main>
      <p class="auth-tagline">{{ 'app.tagline' | transloco }}</p>
    </div>
  `,
  styles: `
    ardea-auth-layout {
      display: block;
      min-height: 100vh;
      min-height: 100dvh;
    }
    .auth-frame {
      display: grid;
      justify-items: center;
      align-content: center;
      gap: var(--gap);
      min-height: 100vh;
      min-height: 100dvh;
      padding: 48px var(--space-lg);
    }
    .auth-brand img {
      height: 30px;
      width: auto;
    }
    .auth-sheet {
      width: min(400px, 100%);
      padding: var(--space-2xl);
      border: 1px solid var(--border);
      border-radius: var(--radius-sheet);
      background: var(--surface);
      animation: auth-rise 420ms var(--ease-out) both;
      view-transition-name: content;
    }
    .auth-tagline {
      color: var(--faint);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
      text-align: center;
    }

    /* Shared by the pages inside */
    .auth-sheet h1 {
      margin-bottom: var(--space-xs);
    }
    .auth-sheet h1 + ui-notice {
      margin-top: var(--space-md);
    }
    .auth-sheet .lead {
      margin-bottom: var(--gap);
      color: var(--muted);
      text-wrap: pretty;
    }
    .auth-form {
      display: grid;
      gap: var(--space-lg);
    }
    .auth-form > ui-notice {
      margin-bottom: var(--space-xs);
    }
    .auth-form > [uiButton],
    .auth-actions {
      margin-top: var(--space-sm);
    }
    .auth-actions {
      display: grid;
      gap: var(--space-sm);
    }
    .auth-foot {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: var(--space-xs) var(--space-lg);
      margin-top: var(--gap);
      padding-top: var(--space-lg);
      border-top: 1px solid var(--rule);
      color: var(--muted);
      text-align: center;
    }
    .auth-steps {
      margin-bottom: var(--space-sm);
      color: var(--muted);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }

    @keyframes auth-rise {
      from {
        opacity: 0;
        transform: translateY(8px);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .auth-sheet {
        animation: auth-fade 200ms ease both;
      }
      @keyframes auth-fade {
        from {
          opacity: 0;
        }
      }
    }
    @media (max-width: 640px) {
      .auth-frame {
        align-content: start;
        gap: var(--space-xl);
        padding: var(--gap) var(--space-md);
      }
      .auth-sheet {
        padding: var(--gap) var(--space-lg);
      }
    }
  `,
})
export class AuthLayout {}
