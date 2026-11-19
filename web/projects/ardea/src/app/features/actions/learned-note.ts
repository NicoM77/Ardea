import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { Session } from '../../core/session/session';

/** What a finding's evidence says about a learned catalogue entry (master plan 6.9), or `null`. */
export interface LearnedHit {
  productKey: string;
  rawKeys: string[];
}

export function learnedHit(evidence: unknown): LearnedHit | null {
  if (!evidence || typeof evidence !== 'object') return null;
  const learned = (evidence as Record<string, unknown>)['learned'];
  if (!learned || typeof learned !== 'object') return null;
  const { productKey, rawKeys } = learned as Record<string, unknown>;
  if (typeof productKey !== 'string') return null;
  return {
    productKey,
    rawKeys: Array.isArray(rawKeys)
      ? rawKeys.filter((k): k is string => typeof k === 'string')
      : [],
  };
}

/**
 * The note on a hit that came about only through a learned catalogue entry (master plan 6.9 «über gelernten
 * Katalogeintrag»): says so, and links the instance administrator to the entry, where it is withdrawn with one click.
 */
@Component({
  selector: 'ardea-learned-note',
  imports: [RouterLink, TranslocoPipe],
  template: `
    @if (hit(); as h) {
      <small class="learned">
        {{ 'findings.learned' | transloco: { key: h.productKey } }}
        @if (isAdmin()) {
          <a
            class="link"
            routerLink="/sources"
            [queryParams]="{ learned: h.rawKeys[0] ?? h.productKey }"
            fragment="catalogue"
            >{{ 'findings.learnedOpen' | transloco }}</a
          >
        }
      </small>
    }
  `,
  styles: `
    .learned {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .link {
      margin-left: var(--space-xs);
    }
  `,
})
export class LearnedNote {
  readonly evidence = input<unknown>(null);

  private readonly session = inject(Session);

  protected readonly hit = computed(() => learnedHit(this.evidence()));
  protected readonly isAdmin = computed(() => this.session.user()?.isInstanceAdmin === true);
}
