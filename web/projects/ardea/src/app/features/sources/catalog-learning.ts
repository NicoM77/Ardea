import { Component, computed, inject, input, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideCircleCheck } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiField, UiInput, UiLoader, UiNotice, UiPill, UiToaster } from 'ui';
import {
  CatalogService,
  getCatalogLearningResource,
  listAiProvidersResource,
  listLearnedEntriesResource,
  type AdoptLearnedRequest,
  type LearnedItemView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Dates } from '../../core/format/dates';
import {
  HOLD_LABELS,
  KINDS,
  LEARNED_BY_LABELS,
  LEARNING_VIEWS,
  OUTCOME_LABELS,
  SCHEMES,
  type LearningView,
  share,
  weeksWithActivity,
} from './learning-format';

@Component({
  selector: 'ardea-catalog-learning',
  imports: [
    TranslocoPipe,
    LucideCircleCheck,
    UiButton,
    UiCard,
    UiField,
    UiInput,
    UiLoader,
    UiNotice,
    UiPill,
  ],
  template: `
    <ui-card [heading]="'catalogLearning.title' | transloco" headingId="catalog-learning">
      <p class="intro">{{ 'catalogLearning.intro' | transloco }}</p>

      @if (status.hasValue() && status.value(); as s) {
        <dl class="measures">
          <div>
            <dt>{{ 'catalogLearning.unknownShare' | transloco }}</dt>
            <dd>
              {{ percent(s.unknownShare) }}
              @if (s.productKeys !== null && s.unknownKeys !== null) {
                <small>{{
                  'catalogLearning.unknownOf'
                    | transloco: { unknown: s.unknownKeys, total: s.productKeys }
                }}</small>
              }
            </dd>
          </div>
          <div>
            <dt>{{ 'catalogLearning.callsToday' | transloco }}</dt>
            <dd>
              {{
                'catalogLearning.callsOf' | transloco: { calls: s.callsToday, quota: s.dailyCalls }
              }}
            </dd>
          </div>
          <div>
            <dt>{{ 'catalogLearning.provider' | transloco }}</dt>
            <dd>
              @if (s.provider.name) {
                {{ s.provider.name }}
                <small>{{
                  (s.provider.local ? 'catalogLearning.local' : 'catalogLearning.cloud') | transloco
                }}</small>
              } @else {
                {{ 'catalogLearning.noProvider' | transloco }}
              }
            </dd>
          </div>
        </dl>

        <div class="controls">
          <ui-field
            [label]="'catalogLearning.providerSetting' | transloco"
            [hint]="
              (s.provider.locked
                ? 'catalogLearning.providerLocked'
                : 'catalogLearning.providerHint'
              ) | transloco
            "
          >
            <select
              uiInput
              [disabled]="s.provider.locked || busy() === 'provider'"
              [value]="s.provider.setting ?? ''"
              (change)="setProvider($event)"
            >
              <option value="">{{ 'catalogLearning.providerDefault' | transloco }}</option>
              @for (p of providers.value() ?? []; track p.name) {
                <option [value]="p.name">
                  {{ p.name }} ·
                  {{ (p.local ? 'catalogLearning.local' : 'catalogLearning.cloud') | transloco }}
                </option>
              }
              <option value="none">{{ 'catalogLearning.providerNone' | transloco }}</option>
            </select>
          </ui-field>
          <div class="buttons">
            <button uiButton type="button" [loading]="busy() === 'run'" (click)="run()">
              {{ 'catalogLearning.run' | transloco }}
            </button>
            <a uiButton variant="ghost" href="/api/v1/instance/catalog/learned/export" download>
              {{ 'catalogLearning.export' | transloco }}
            </a>
          </div>
        </div>

        @if (active(s.weeks)) {
          <table class="weeks">
            <caption>
              {{
                'catalogLearning.weeksCaption' | transloco
              }}
            </caption>
            <thead>
              <tr>
                <th scope="col">{{ 'catalogLearning.week' | transloco }}</th>
                <th scope="col">{{ 'catalogLearning.learned' | transloco }}</th>
                <th scope="col">{{ 'catalogLearning.adopted' | transloco }}</th>
                <th scope="col">{{ 'catalogLearning.confirmed' | transloco }}</th>
                <th scope="col">{{ 'catalogLearning.rejected' | transloco }}</th>
                <th scope="col">{{ 'catalogLearning.withdrawn' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (w of s.weeks; track w.week) {
                <tr>
                  <th scope="row">{{ dates.date(w.week) }}</th>
                  <td>{{ w.learned }}</td>
                  <td>{{ w.adopted }}</td>
                  <td>{{ w.confirmed }}</td>
                  <td>{{ w.rejected }}</td>
                  <td>{{ w.withdrawn }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      } @else if (status.error()) {
        <ui-notice tone="error">{{ 'catalogLearning.loadFailed' | transloco }}</ui-notice>
      }

      @if (problem()) {
        <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
      }

      @if (rawKey()) {
        <p class="filter">
          {{ 'catalogLearning.filtered' | transloco: { key: rawKey() } }}
        </p>
      } @else {
        <div class="views" role="group" [attr.aria-label]="'catalogLearning.views' | transloco">
          @for (v of views; track v.value) {
            <button
              uiButton
              variant="ghost"
              type="button"
              [attr.aria-pressed]="view() === v.value"
              (click)="view.set(v.value)"
            >
              {{ v.label | transloco }}
              @if (count(v.value); as n) {
                <span class="count">{{ n }}</span>
              }
            </button>
          }
        </div>
      }

      @if (items.hasValue() && items.value(); as list) {
        @if (list.length) {
          <ul class="items">
            @for (item of list; track item.id) {
              <li>
                <div class="what">
                  <b>{{ item.vendor ? item.vendor + ' · ' : '' }}{{ item.product }}</b>
                  <span class="mono">{{ item.rawKey }}</span>
                  <span class="sub">{{
                    'catalogLearning.records' | transloco: { count: item.recordCount }
                  }}</span>
                  @if (item.inKev) {
                    <ui-pill tone="alert">{{ 'catalogLearning.kev' | transloco }}</ui-pill>
                  }
                  <ui-pill>{{ outcomeLabel(item.outcome) | transloco }}</ui-pill>
                </div>
                @if (item.proposal; as p) {
                  <p class="proposal">
                    @switch (p.decision) {
                      @case ('alias') {
                        {{
                          'catalogLearning.aliasOf'
                            | transloco: { name: p.aliasName ?? p.aliasOf, key: p.aliasOf }
                        }}
                      }
                      @case ('new') {
                        {{
                          'catalogLearning.newProduct'
                            | transloco
                              : {
                                  name: p.newProduct?.name,
                                  key: p.newProduct?.key,
                                  kind: p.newProduct?.kind,
                                  scheme: p.newProduct?.versionScheme,
                                }
                        }}
                      }
                      @default {
                        {{ 'catalogLearning.unclear' | transloco }}
                      }
                    }
                  </p>
                  <p class="sub">
                    {{ learnedByLabel(p.learnedBy) | transloco }}
                    @if (p.model) {
                      ({{ p.provider }} · {{ p.model }} ·
                      {{ 'catalogLearning.prompt' | transloco: { version: p.promptVersion } }})
                    }
                    · {{ 'catalogLearning.confidence' | transloco: { confidence: p.confidence } }}
                    @if (p.hold) {
                      · {{ holdLabel(p.hold) | transloco }}
                    }
                  </p>
                  @if (p.reasoning) {
                    <p class="reason">{{ p.reasoning }}</p>
                  }
                }
                @if (item.cpePairs.length || item.purl) {
                  <p class="sub mono">
                    {{ item.cpePairs.concat(item.purl ? [item.purl] : []).join(', ') }}
                  </p>
                }

                <div class="act">
                  @if (decidable(item)) {
                    @if (item.proposal?.decision === 'alias' || item.proposal?.decision === 'new') {
                      <button
                        uiButton
                        type="button"
                        [loading]="busy() === item.id"
                        (click)="adopt(item)"
                      >
                        {{ 'catalogLearning.confirm' | transloco }}
                      </button>
                    }
                    <button
                      uiButton
                      variant="ghost"
                      type="button"
                      [attr.aria-expanded]="editing() === item.id"
                      (click)="edit(item)"
                    >
                      {{ 'catalogLearning.change' | transloco }}
                    </button>
                    <button uiButton variant="ghost" type="button" (click)="reject(item)">
                      {{ 'catalogLearning.reject' | transloco }}
                    </button>
                  } @else if (item.outcome === 'alias' || item.outcome === 'new') {
                    <button
                      uiButton
                      variant="ghost"
                      type="button"
                      [loading]="busy() === item.id"
                      (click)="withdraw(item)"
                    >
                      {{ 'catalogLearning.withdraw' | transloco }}
                    </button>
                  }
                </div>

                @if (editing() === item.id) {
                  <form class="change" (submit)="$event.preventDefault(); adopt(item, change())">
                    <fieldset>
                      <legend>{{ 'catalogLearning.changeLegend' | transloco }}</legend>
                      <label
                        ><input
                          type="radio"
                          name="mode"
                          value="alias"
                          [checked]="mode() === 'alias'"
                          (change)="mode.set('alias')"
                        />
                        {{ 'catalogLearning.modeAlias' | transloco }}</label
                      >
                      <label
                        ><input
                          type="radio"
                          name="mode"
                          value="new"
                          [checked]="mode() === 'new'"
                          (change)="mode.set('new')"
                        />
                        {{ 'catalogLearning.modeNew' | transloco }}</label
                      >
                    </fieldset>
                    @if (mode() === 'alias') {
                      <ui-field
                        [label]="'catalogLearning.aliasKey' | transloco"
                        [hint]="'catalogLearning.aliasKeyHint' | transloco"
                      >
                        <input
                          uiInput
                          type="text"
                          spellcheck="false"
                          [value]="aliasKey()"
                          (input)="aliasKey.set(text($event))"
                        />
                      </ui-field>
                    } @else {
                      <ui-field [label]="'catalogLearning.name' | transloco">
                        <input
                          uiInput
                          type="text"
                          [value]="name()"
                          (input)="name.set(text($event))"
                        />
                      </ui-field>
                      <ui-field [label]="'catalogLearning.vendor' | transloco" optional>
                        <input
                          uiInput
                          type="text"
                          [value]="vendor()"
                          (input)="vendor.set(text($event))"
                        />
                      </ui-field>
                      <ui-field [label]="'catalogLearning.kind' | transloco">
                        <select uiInput [value]="kind()" (change)="kind.set(text($event))">
                          @for (k of kinds; track k) {
                            <option [value]="k">{{ k }}</option>
                          }
                        </select>
                      </ui-field>
                      <ui-field [label]="'catalogLearning.scheme' | transloco">
                        <select uiInput [value]="scheme()" (change)="scheme.set(text($event))">
                          @for (k of schemes; track k) {
                            <option [value]="k">{{ k }}</option>
                          }
                        </select>
                      </ui-field>
                    }
                    <div class="buttons">
                      <button uiButton type="submit" [loading]="busy() === item.id">
                        {{ 'catalogLearning.save' | transloco }}
                      </button>
                      <button uiButton variant="ghost" type="button" (click)="editing.set(null)">
                        {{ 'catalogLearning.cancel' | transloco }}
                      </button>
                    </div>
                  </form>
                }
              </li>
            }
          </ul>
        } @else {
          <p class="healthy">
            <svg lucideCircleCheck aria-hidden="true"></svg>
            {{ 'catalogLearning.empty' | transloco }}
          </p>
        }
      } @else if (items.error()) {
        <ui-notice tone="error">{{ 'catalogLearning.loadFailed' | transloco }}</ui-notice>
      } @else {
        <ui-loader [label]="'catalogLearning.loading' | transloco" />
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .intro,
    .filter {
      margin: calc(-1 * var(--space-sm)) 0 var(--space-md);
      color: var(--muted);
      max-width: 72ch;
    }
    .measures {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: var(--space-md) var(--gap);
      margin: 0 0 var(--space-md);
    }
    .measures dt {
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    .measures dd {
      margin: 0;
      font-weight: 600;
    }
    .measures small,
    .sub {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      font-weight: 400;
    }
    .controls {
      display: grid;
      grid-template-columns: minmax(0, 320px) auto;
      align-items: end;
      gap: var(--gap);
      margin-bottom: var(--space-md);
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .weeks {
      width: 100%;
      border-collapse: collapse;
      margin-bottom: var(--space-md);
      font-size: var(--fs-sub);
      font-variant-numeric: tabular-nums;
    }
    .weeks caption {
      text-align: left;
      color: var(--muted);
      padding-bottom: var(--space-xs);
    }
    .weeks th,
    .weeks td {
      padding: var(--space-xs) var(--space-sm);
      border-top: 1px solid var(--rule);
      text-align: right;
    }
    .weeks th:first-child {
      text-align: left;
      font-weight: 400;
    }
    .healthy {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      color: var(--muted);
    }
    .healthy svg {
      flex: none;
      width: 16px;
      height: 16px;
      color: var(--positive);
    }
    .gap {
      display: block;
      margin-bottom: var(--space-md);
    }
    .views {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-xs);
      margin-bottom: var(--space-sm);
    }
    .views [aria-pressed='true'] {
      background: var(--raised);
    }
    .count {
      margin-left: var(--space-xs);
      color: var(--muted);
      font-variant-numeric: tabular-nums;
    }
    .items {
      display: grid;
    }
    .items > li {
      display: grid;
      gap: var(--space-xs);
      padding: var(--space-md) 0;
      border-top: 1px solid var(--rule);
      min-width: 0;
    }
    .what {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-xs) var(--space-md);
      min-width: 0;
    }
    .what b,
    .proposal,
    .reason {
      overflow-wrap: anywhere;
    }
    .proposal,
    .reason {
      margin: 0;
    }
    .reason {
      color: var(--muted);
      font-style: italic;
    }
    .mono {
      font-family: var(--mono);
      font-size: var(--fs-sub);
      overflow-wrap: anywhere;
    }
    .act {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .change {
      display: grid;
      gap: var(--space-md);
      max-width: 480px;
      padding: var(--space-md);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
    }
    .change fieldset {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-md);
      margin: 0;
      padding: 0;
      border: 0;
    }
    .change legend {
      margin-bottom: var(--space-xs);
      font-weight: 600;
    }
    .change label {
      display: inline-flex;
      align-items: center;
      gap: var(--space-xs);
    }
    @media (max-width: 640px) {
      .measures,
      .controls {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class CatalogLearning {
  readonly rawKey = input<string>('');

  private readonly api = inject(CatalogService);
  private readonly transloco = inject(TranslocoService);
  private readonly toaster = inject(UiToaster);
  protected readonly dates = inject(Dates);
  protected readonly active = weeksWithActivity;

  protected readonly views = LEARNING_VIEWS;
  protected readonly kinds = KINDS;
  protected readonly schemes = SCHEMES;

  protected readonly status = getCatalogLearningResource();
  protected readonly providers = listAiProvidersResource();
  protected readonly view = signal<LearningView>('review');
  protected readonly items = listLearnedEntriesResource(
    computed(() => (this.rawKey() ? { rawKey: this.rawKey() } : { view: this.view() })),
  );

  protected readonly busy = signal<string | null>(null);
  protected readonly problem = signal<string | null>(null);

  protected readonly editing = signal<string | null>(null);
  protected readonly mode = signal<'alias' | 'new'>('alias');
  protected readonly aliasKey = signal('');
  protected readonly name = signal('');
  protected readonly vendor = signal('');
  protected readonly kind = signal('other');
  protected readonly scheme = signal('numeric');

  protected readonly change = computed<AdoptLearnedRequest>(() =>
    this.mode() === 'alias'
      ? { aliasOf: this.aliasKey().trim(), newProduct: null }
      : {
          aliasOf: null,
          newProduct: {
            name: this.name().trim(),
            vendor: this.vendor().trim() || null,
            kind: this.kind(),
            versionScheme: this.scheme(),
            cpe: null,
            related: null,
            platform: null,
          },
        },
  );

  protected count(view: LearningView): number | null {
    const outcomes = (this.status.value()?.outcomes ?? {}) as Record<string, number | string>;
    const of = (k: string) => Number(outcomes[k] ?? 0);
    const n =
      view === 'review'
        ? of('pending') + of('unclear')
        : view === 'open'
          ? of('open')
          : view === 'in_effect'
            ? of('alias') + of('new')
            : of('rejected') + of('withdrawn');
    return n > 0 ? n : null;
  }

  protected decidable(item: LearnedItemView): boolean {
    return item.outcome === 'open' || item.outcome === 'pending' || item.outcome === 'unclear';
  }

  protected edit(item: LearnedItemView): void {
    const p = item.proposal;
    this.mode.set(p?.decision === 'new' ? 'new' : 'alias');
    this.aliasKey.set(p?.aliasOf ?? '');
    this.name.set(p?.newProduct?.name ?? item.product);
    this.vendor.set(p?.newProduct?.vendor ?? item.vendor ?? '');
    this.kind.set(p?.newProduct?.kind ?? 'other');
    this.scheme.set(p?.newProduct?.versionScheme ?? 'numeric');
    this.editing.set(this.editing() === item.id ? null : item.id);
  }

  protected adopt(
    item: LearnedItemView,
    request: AdoptLearnedRequest = { aliasOf: null, newProduct: null },
  ): Promise<void> {
    return this.act(item.id, item.rawKey, 'catalogLearning.adoptedNotice', () =>
      firstValueFrom(this.api.adoptLearnedEntry(item.id, request)),
    );
  }

  protected reject(item: LearnedItemView): Promise<void> {
    return this.act(item.id, item.rawKey, 'catalogLearning.rejectedNotice', () =>
      firstValueFrom(this.api.rejectLearnedEntry(item.id)),
    );
  }

  protected withdraw(item: LearnedItemView): Promise<void> {
    return this.act(item.id, item.rawKey, 'catalogLearning.withdrawnNotice', () =>
      firstValueFrom(this.api.withdrawLearnedEntry(item.id)),
    );
  }

  protected run(): Promise<void> {
    return this.act('run', '', 'catalogLearning.runNotice', () =>
      firstValueFrom(this.api.runCatalogLearning()),
    );
  }

  protected setProvider(event: Event): Promise<void> {
    const provider = (event.target as HTMLSelectElement).value;
    const before = this.status.value()?.provider.setting ?? '';
    return this.act(
      'provider',
      provider,
      'catalogLearning.providerNotice',
      () => firstValueFrom(this.api.updateCatalogLearning({ provider })),
      () =>
        void this.act('provider', before, null, () =>
          firstValueFrom(this.api.updateCatalogLearning({ provider: before })),
        ),
    );
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected percent(value: number | string | null): string {
    return share(value, this.transloco.getActiveLang());
  }

  protected outcomeLabel(outcome: string): string {
    return OUTCOME_LABELS[outcome] ?? 'catalogLearning.outcome.open';
  }

  protected learnedByLabel(by: string): string {
    return LEARNED_BY_LABELS[by] ?? 'catalogLearning.by.rule';
  }

  protected holdLabel(hold: string): string {
    return HOLD_LABELS[hold] ?? 'catalogLearning.hold.check';
  }

  private async act(
    busy: string,
    key: string,
    notice: string | null,
    work: () => Promise<unknown>,
    undo?: () => void,
  ): Promise<void> {
    this.busy.set(busy);
    this.problem.set(null);
    try {
      await work();
      if (notice) this.toaster.show({ text: this.transloco.translate(notice, { key }), undo });
      this.editing.set(null);
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, {
          codes: {
            learned_entry_invalid: 'catalogLearning.invalid',
            learned_entry_decided: 'catalogLearning.decided',
            learned_entry_not_in_effect: 'catalogLearning.decided',
            setting_locked: 'catalogLearning.providerLocked',
          },
          statuses: { 404: 'catalogLearning.gone' },
          fallback: 'catalogLearning.failed',
        }),
      );
    } finally {
      this.busy.set(null);
      this.items.reload();
      this.status.reload();
    }
  }
}
