import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiButton, UiCard, UiLoader, UiNotice } from 'ui';
import {
  listAuditEventsResource,
  type AuditEventView,
  type ListAuditEventsParams,
  type SessionOrganization,
} from '../../core/api/client';
import { actorKey, AUDIT_ACTIONS, dataSummary } from './audit-format';

const PAGE = 50;

@Component({
  selector: 'ardea-audit-card',
  imports: [TranslocoPipe, UiButton, UiCard, UiLoader, UiNotice],
  template: `
    <ui-card [heading]="'settings.audit.title' | transloco" [attr.aria-busy]="page.isLoading()">
      <p class="intro">{{ 'settings.audit.intro' | transloco }}</p>
      @if (events().length) {
        <ol class="events" [attr.aria-label]="'settings.audit.title' | transloco">
          @for (event of events(); track event.id) {
            <li>
              <time [attr.datetime]="event.at">{{ when(event.at) }}</time>
              <div class="what">
                <span>
                  @if (actor(event).name; as name) {
                    <b>{{ name }}</b>
                  } @else {
                    <b class="muted">{{ actor(event).key! | transloco }}</b>
                  }
                  {{ label(event) }}
                </span>
                @if (detail(event); as text) {
                  <small>{{ text }}</small>
                }
              </div>
            </li>
          }
        </ol>
      } @else if (page.hasValue()) {
        <p class="muted">{{ 'settings.audit.none' | transloco }}</p>
      }
      @if (page.error()) {
        <ui-notice tone="error">{{ 'settings.audit.loadFailed' | transloco }}</ui-notice>
      } @else if (page.isLoading()) {
        <ui-loader [label]="'settings.audit.loading' | transloco" />
      } @else if (next()) {
        <div class="more">
          <button uiButton type="button" (click)="cursor.set(next())">
            {{ 'settings.audit.more' | transloco }}
          </button>
        </div>
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .intro,
    .muted,
    small {
      color: var(--muted);
    }
    .intro {
      margin-bottom: var(--space-md);
    }
    .events li {
      display: grid;
      grid-template-columns: 150px minmax(0, 1fr);
      gap: var(--space-md);
      padding: var(--space-sm) 0;
      border-top: 1px solid var(--rule);
    }
    .events li:first-child {
      border-top: 0;
    }
    time {
      color: var(--muted);
      font-size: var(--fs-sub);
      white-space: nowrap;
    }
    .what {
      display: grid;
      min-width: 0;
    }
    b {
      font-weight: 500;
    }
    small {
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      overflow-wrap: anywhere;
    }
    .more {
      margin-top: var(--space-md);
    }
    @media (max-width: 640px) {
      .events li {
        grid-template-columns: minmax(0, 1fr);
        gap: 2px;
      }
    }
  `,
})
export class AuditCard {
  readonly organization = input.required<SessionOrganization>();

  private readonly transloco = inject(TranslocoService);
  protected readonly cursor = signal<string | null>(null);
  private readonly params = computed<ListAuditEventsParams>(() =>
    this.cursor() ? { limit: PAGE, cursor: this.cursor()! } : { limit: PAGE },
  );
  protected readonly page = listAuditEventsResource(
    computed(() => this.organization().id),
    this.params,
  );

  protected readonly events = linkedSignal<AuditEventView[], AuditEventView[]>({
    source: () => (this.page.hasValue() ? (this.page.value()?.items ?? []) : []),
    computation: (items, previous) => {
      if (!this.cursor() || !previous) return items;
      const seen = new Set(previous.value.map((e) => e.id));
      return [...previous.value, ...items.filter((e) => !seen.has(e.id))];
    },
  });
  protected readonly next = computed(() =>
    this.page.hasValue() ? (this.page.value()?.nextCursor ?? null) : null,
  );

  protected readonly actor = actorKey;

  protected label(event: AuditEventView): string {
    const key = AUDIT_ACTIONS[event.action];
    return key ? this.transloco.translate(key) : event.action;
  }

  protected detail(event: AuditEventView): string {
    return dataSummary(event.data);
  }

  protected when(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }
}
