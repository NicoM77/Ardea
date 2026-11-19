import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucidePlugZap, LucideSparkles } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiEmptyState, UiLoader, UiNotice, UiPill } from 'ui';
import {
  AdministrationService,
  listAiProvidersResource,
  listModelQualityResource,
  type AiConnectionResult,
  type AiProviderInfo,
  type ModelQualityView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { count } from './settings-patch';

/** The outcome of one connection test, kept per provider until the next. */
interface TestOutcome {
  ok: boolean;
  durationMs: number;
  /** The API's own words for an administrator, or a translation key when the request failed. */
  error: string | null;
  errorKey: string | null;
}

/**
 * The language models of this instance (task 4.1, master plan 7.8, 11.4): the configured
 * providers with kind, model, whether they run on the instance's own network, and their host,
 * each with a connection test; and how people judged the models' verdicts (task 4.7, master plan
 * 7.9): the correction rate per model and prompt version. Providers are configured through
 * environment variables, so the card shows them and changes nothing.
 */
@Component({
  selector: 'ardea-ai-card',
  imports: [
    TranslocoPipe,
    LucidePlugZap,
    LucideSparkles,
    UiButton,
    UiCard,
    UiEmptyState,
    UiLoader,
    UiNotice,
    UiPill,
  ],
  template: `
    <ui-card [heading]="'admin.ai.title' | transloco" headingId="admin-ai">
      <p class="intro">{{ 'admin.ai.intro' | transloco }}</p>
      @if (providers.error()) {
        <ui-notice tone="error">{{ 'admin.ai.loadFailed' | transloco }}</ui-notice>
        <div class="gap">
          <button uiButton type="button" (click)="providers.reload()">
            {{ 'admin.tryAgain' | transloco }}
          </button>
        </div>
      } @else if (providers.hasValue()) {
        @if (list().length === 0) {
          <ui-empty-state [heading]="'admin.ai.emptyHeading' | transloco">
            <svg uiEmptyIcon lucideSparkles aria-hidden="true"></svg>
            {{ 'admin.ai.emptyText' | transloco }}
          </ui-empty-state>
        } @else {
          <ul class="providers">
            @for (p of list(); track p.name) {
              <li class="provider">
                <div class="who">
                  <b>{{ p.name }}</b>
                  <ui-pill [tone]="p.local ? 'done' : 'neutral'">{{
                    (p.local ? 'admin.ai.local' : 'admin.ai.cloud') | transloco
                  }}</ui-pill>
                </div>
                <dl class="facts">
                  <div>
                    <dt>{{ 'admin.ai.kind' | transloco }}</dt>
                    <dd>{{ p.kind }}</dd>
                  </div>
                  <div>
                    <dt>{{ 'admin.ai.model' | transloco }}</dt>
                    <dd class="id">{{ p.model }}</dd>
                  </div>
                  <div>
                    <dt>{{ 'admin.ai.host' | transloco }}</dt>
                    <dd class="id">{{ p.host ?? ('admin.ai.defaultHost' | transloco) }}</dd>
                  </div>
                </dl>
                <div class="test">
                  <button
                    uiButton
                    type="button"
                    [loading]="testing() === p.name"
                    [disabled]="testing() !== null && testing() !== p.name"
                    [attr.aria-label]="'admin.ai.testLabel' | transloco: { name: p.name }"
                    (click)="test(p)"
                  >
                    <svg lucidePlugZap aria-hidden="true"></svg>
                    {{ 'admin.ai.test' | transloco }}
                  </button>
                  <div class="result" role="status">
                    @if (outcomes()[p.name]; as o) {
                      @if (o.ok) {
                        <ui-notice tone="success">{{
                          'admin.ai.testOk' | transloco: { ms: o.durationMs }
                        }}</ui-notice>
                      } @else {
                        <ui-notice tone="error">
                          {{ 'admin.ai.testFailed' | transloco }}
                          @if (o.error) {
                            {{ o.error }}
                          } @else if (o.errorKey) {
                            {{ o.errorKey | transloco }}
                          }
                        </ui-notice>
                      }
                    }
                  </div>
                </div>
              </li>
            }
          </ul>
        }
      } @else {
        <ui-loader [label]="'admin.ai.loading' | transloco" />
      }

      <h3 class="h3">{{ 'admin.ai.quality.title' | transloco }}</h3>
      <p class="intro">{{ 'admin.ai.quality.intro' | transloco }}</p>
      @if (quality.error()) {
        <ui-notice tone="error">{{ 'admin.ai.quality.loadFailed' | transloco }}</ui-notice>
      } @else if (quality.hasValue()) {
        @if (rows().length === 0) {
          <p class="none">{{ 'admin.ai.quality.none' | transloco }}</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">
                {{
                  'admin.ai.quality.title' | transloco
                }}
              </caption>
              <thead>
                <tr>
                  <th scope="col">{{ 'admin.ai.model' | transloco }}</th>
                  <th scope="col">{{ 'admin.ai.quality.prompt' | transloco }}</th>
                  <th scope="col" class="num">{{ 'admin.ai.quality.assessments' | transloco }}</th>
                  <th scope="col" class="num">{{ 'admin.ai.quality.verdicts' | transloco }}</th>
                  <th scope="col" class="num">{{ 'admin.ai.quality.confirmed' | transloco }}</th>
                  <th scope="col" class="num">{{ 'admin.ai.quality.corrected' | transloco }}</th>
                  <th scope="col" class="num">{{ 'admin.ai.quality.rate' | transloco }}</th>
                </tr>
              </thead>
              <tbody>
                @for (r of rows(); track $index) {
                  <tr>
                    <td>
                      <span class="id">{{ r.model ?? '–' }}</span>
                      @if (r.provider) {
                        <small>{{ r.provider }}</small>
                      }
                    </td>
                    <td class="id">{{ r.promptVersion ?? '–' }}</td>
                    <td class="num">{{ number(r.assessments) }}</td>
                    <td class="num">{{ number(r.verdicts) }}</td>
                    <td class="num">{{ number(r.confirmed) }}</td>
                    <td class="num">{{ number(r.corrected) }}</td>
                    <td class="num">{{ rate(r) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      } @else {
        <ui-loader [label]="'admin.ai.loading' | transloco" />
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .intro,
    .none {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .intro {
      margin-bottom: var(--space-lg);
    }
    .gap {
      margin-top: var(--space-md);
    }
    .providers {
      display: grid;
      gap: 0;
    }
    .provider {
      display: grid;
      grid-template-columns: minmax(140px, 1fr) minmax(0, 3fr) minmax(220px, 2fr);
      gap: var(--space-md) var(--gap);
      align-items: start;
      padding: var(--space-md) 0;
      border-top: 1px solid var(--rule);
    }
    .who {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-sm);
      min-width: 0;
    }
    .who b {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .facts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm) var(--space-xl);
      min-width: 0;
    }
    dt {
      color: var(--muted);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    dd {
      margin-top: 2px;
      overflow-wrap: anywhere;
    }
    .test {
      display: grid;
      gap: var(--space-sm);
      justify-items: start;
    }
    .result:empty {
      display: none;
    }
    .h3 {
      margin: var(--space-xl) 0 var(--space-xs);
      font-size: var(--fs-body);
      font-weight: 500;
    }
    .table-wrap {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th {
      height: 32px;
      padding-right: var(--space-md);
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 400;
      text-align: left;
      white-space: nowrap;
    }
    td {
      padding: var(--space-sm) var(--space-md) var(--space-sm) 0;
      border-top: 1px solid var(--rule);
      vertical-align: top;
    }
    td small {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    .num {
      padding-right: 0;
      padding-left: var(--space-md);
      text-align: right;
      white-space: nowrap;
    }
    @media (max-width: 900px) {
      .provider {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class AiCard {
  private readonly admin = inject(AdministrationService);
  private readonly transloco = inject(TranslocoService);

  protected readonly providers = listAiProvidersResource();
  protected readonly quality = listModelQualityResource();
  protected readonly list = computed<readonly AiProviderInfo[]>(() =>
    this.providers.hasValue() ? (this.providers.value() ?? []) : [],
  );
  protected readonly rows = computed<readonly ModelQualityView[]>(() =>
    this.quality.hasValue() ? (this.quality.value() ?? []) : [],
  );
  protected readonly testing = signal<string | null>(null);
  protected readonly outcomes = signal<Readonly<Record<string, TestOutcome>>>({});
  protected readonly number = count;

  protected async test(provider: AiProviderInfo): Promise<void> {
    if (this.testing()) return;
    this.testing.set(provider.name);
    let outcome: TestOutcome;
    try {
      const result: AiConnectionResult = await firstValueFrom(
        this.admin.testAiProvider(provider.name),
      );
      outcome = {
        ok: result.ok,
        durationMs: Number(result.durationMs),
        error: result.error,
        errorKey: null,
      };
    } catch (error) {
      outcome = {
        ok: false,
        durationMs: 0,
        error: null,
        errorKey: problemMessageKey(error, {
          codes: { ai_provider_not_found: 'admin.ai.notFound' },
          fallback: 'admin.ai.testError',
        }),
      };
    } finally {
      this.testing.set(null);
    }
    this.outcomes.update((all) => ({ ...all, [provider.name]: outcome }));
  }

  /** Corrected among the reviewed, as a percentage in the reader's language; a dash before the first review. */
  protected rate(row: ModelQualityView): string {
    if (row.correctionRate === null || row.correctionRate === undefined) return '–';
    return new Intl.NumberFormat(this.transloco.getActiveLang(), {
      style: 'percent',
      maximumFractionDigits: 1,
    }).format(Number(row.correctionRate));
  }
}
