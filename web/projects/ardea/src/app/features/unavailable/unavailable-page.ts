import { Component, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideWifiOff } from '@lucide/angular';
import { UiButton } from 'ui';
import { safeReturnUrl } from '../../core/session/access';
import { Session } from '../../core/session/session';

/** The API did not answer: say so plainly and offer to try again, instead of a blank screen. */
@Component({
  selector: 'ardea-unavailable-page',
  imports: [TranslocoPipe, LucideWifiOff, UiButton],
  template: `
    <div class="mark"><svg lucideWifiOff aria-hidden="true"></svg></div>
    <h1>{{ 'unavailable.title' | transloco }}</h1>
    <p class="lead">{{ 'unavailable.text' | transloco }}</p>
    <button uiButton variant="primary" block type="button" [loading]="retrying()" (click)="retry()">
      {{ 'unavailable.retry' | transloco }}
    </button>
  `,
  styles: `
    .mark {
      display: grid;
      place-items: center;
      width: 40px;
      height: 40px;
      margin-bottom: var(--space-lg);
      border-radius: 50%;
      background: var(--raised);
      color: var(--muted);
    }
    .mark svg {
      width: 20px;
      height: 20px;
    }
  `,
})
export class UnavailablePage {
  readonly returnUrl = input<string | null>(null);

  private readonly session = inject(Session);
  private readonly router = inject(Router);
  protected readonly retrying = signal(false);

  protected async retry(): Promise<void> {
    this.retrying.set(true);
    await this.session.refresh();
    this.retrying.set(false);
    if (this.session.state() !== 'unavailable') {
      await this.router.navigateByUrl(safeReturnUrl(this.returnUrl()) ?? '/');
    }
  }
}
