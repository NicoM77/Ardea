import { Component, computed, effect, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { LiveEvents } from '../../core/live/live-events';
import { Session } from '../../core/session/session';
import { MfaRequired } from './mfa-required';
import { mfaBlocked, organizationBySlug } from './org-format';

/**
 * Around every page below `/o/:slug`. An organization that requires two-factor authentication
 * shows its pages only to members who use it; the others see why and how to
 * turn it on, instead of pages whose every request the API refuses.
 *
 * While the pages show, the frame holds the organization's live stream (task 3.11), so counts and
 * lists stay current without polling.
 */
@Component({
  selector: 'ardea-organization-frame',
  imports: [RouterOutlet, MfaRequired],
  template: `
    @if (blocked() && organization(); as org) {
      <ardea-mfa-required [organization]="org.name" [returnUrl]="url()" />
    } @else {
      <router-outlet />
    }
  `,
})
export class OrganizationFrame {
  /** From the route. */
  readonly slug = input.required<string>();

  private readonly session = inject(Session);
  private readonly router = inject(Router);

  protected readonly organization = computed(() =>
    organizationBySlug(this.session.user(), this.slug()),
  );
  protected readonly blocked = computed(() => mfaBlocked(this.session.user(), this.organization()));
  protected readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  constructor() {
    const live = inject(LiveEvents);
    effect((onCleanup) => {
      const id = this.organization()?.id;
      if (!id || this.blocked()) return;
      onCleanup(live.connect(id));
    });
  }
}
