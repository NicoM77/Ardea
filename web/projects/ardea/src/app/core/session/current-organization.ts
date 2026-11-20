import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import type { SessionOrganization } from '../api/client';
import { Session } from './session';

const SLUG = /^\/o\/([^/?#]+)/;
const STORAGE_KEY = 'ardea.organization';

export function slugOfUrl(url: string): string | null {
  return SLUG.exec(url)?.[1] ?? null;
}

export function pickOrganization(
  organizations: readonly SessionOrganization[],
  ...slugs: (string | null)[]
): SessionOrganization | null {
  for (const slug of slugs) {
    const match = organizations.find((org) => org.slug === slug);
    if (match) return match;
  }
  return organizations[0] ?? null;
}

@Injectable({ providedIn: 'root' })
export class CurrentOrganization {
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly last = signal(readLast());
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  readonly organization = computed(() =>
    pickOrganization(this.session.user()?.organizations ?? [], slugOfUrl(this.url()), this.last()),
  );

  constructor() {
    effect(() => {
      const slug = this.organization()?.slug;
      if (!slug) return;
      this.last.set(slug);
      writeLast(slug);
    });
  }
}

function readLast(): string | null {
  try {
    return sessionStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function writeLast(slug: string): void {
  try {
    sessionStorage.setItem(STORAGE_KEY, slug);
  } catch {
    return;
  }
}
