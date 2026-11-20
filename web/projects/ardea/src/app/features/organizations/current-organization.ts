import { computed, inject, Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import type { SessionOrganization } from '../../core/api/client';
import { Session } from '../../core/session/session';
import { organizationBySlug } from './org-format';

export function injectOrganization(): {
  slug: Signal<string | null>;
  organization: Signal<SessionOrganization | null>;
} {
  const session = inject(Session);
  let route: ActivatedRoute | null = inject(ActivatedRoute);
  while (route && !route.snapshot.paramMap.has('slug')) route = route.parent;
  const source = route ?? inject(ActivatedRoute);
  const slug = toSignal(source.paramMap.pipe(map((params) => params.get('slug'))), {
    initialValue: source.snapshot.paramMap.get('slug'),
  });
  const organization = computed(() => organizationBySlug(session.user(), slug()));
  return { slug, organization };
}
