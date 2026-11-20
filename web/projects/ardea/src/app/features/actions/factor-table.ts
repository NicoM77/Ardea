import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiPriority } from 'ui';
import { priorityOfLevel } from './action-format';

export interface FactorRow {
  factor: string;
  value: string;
  evidence?: string | null;
  points: number;
}
export interface Bound {
  bound: string;
  level: number;
}
export interface RuleResult {
  rulesetVersion: number;
  level: number;
  exclusion?: string | null;
  factors: FactorRow[];
  points?: number | null;
  caps: Bound[];
  floors: Bound[];
}

export function ruleResultOf(json: unknown): RuleResult | null {
  if (!json || typeof json !== 'object') return null;
  const r = json as Partial<RuleResult>;
  return {
    rulesetVersion: Number(r.rulesetVersion ?? 1),
    level: Number(r.level ?? 1),
    exclusion: r.exclusion ?? null,
    factors: Array.isArray(r.factors) ? r.factors : [],
    points: r.points ?? null,
    caps: Array.isArray(r.caps) ? r.caps : [],
    floors: Array.isArray(r.floors) ? r.floors : [],
  };
}

@Component({
  selector: 'ardea-factor-table',
  imports: [TranslocoPipe, UiPriority],
  template: `
    @if (result(); as r) {
      @if (r.exclusion) {
        <p class="excluded">
          {{ 'factors.excluded' | transloco: { reason: r.exclusion } }}
        </p>
      } @else {
        <table>
          <caption class="sr-only">
            {{
              caption()
            }}
          </caption>
          <thead>
            <tr>
              <th scope="col">{{ 'factors.factor' | transloco }}</th>
              <th scope="col">{{ 'factors.evidence' | transloco }}</th>
              <th scope="col">{{ 'factors.points' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of r.factors; track row.factor) {
              <tr [class.zero]="row.points === 0">
                <th scope="row">{{ 'factors.name' | transloco: { factor: row.factor } }}</th>
                <td class="muted">
                  {{ 'factors.value' | transloco: { value: row.value } }}
                  @if (row.evidence) {
                    · {{ row.evidence }}
                  }
                </td>
                <td class="pts">{{ row.points > 0 ? '+' + row.points : row.points }}</td>
              </tr>
            }
          </tbody>
          <tfoot>
            <tr>
              <td colspan="2">
                {{
                  'factors.sum' | transloco: { points: r.points ?? 0, version: r.rulesetVersion }
                }}
                @for (b of bounds(); track b.bound) {
                  <span class="bound">
                    · {{ 'factors.bound' | transloco: { bound: b.bound } }}
                  </span>
                }
              </td>
              <td class="pts"><ui-priority [level]="level()" /></td>
            </tr>
          </tfoot>
        </table>
      }
    }
  `,
  styles: `
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    th {
      height: 28px;
      padding-right: var(--space-md);
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 400;
      text-align: left;
    }
    tbody th {
      color: var(--text);
      font-size: var(--fs-sub);
      white-space: nowrap;
    }
    td,
    tbody th {
      padding: 6px var(--space-md) 6px 0;
      border-top: 1px solid var(--rule);
      vertical-align: top;
    }
    .muted {
      color: var(--muted);
      overflow-wrap: anywhere;
    }
    .pts,
    th:last-child {
      padding-right: 0;
      text-align: right;
      white-space: nowrap;
    }
    .pts {
      font-weight: 500;
    }
    .zero .pts {
      color: var(--faint);
      font-weight: 400;
    }
    tfoot td {
      font-weight: 500;
    }
    .bound {
      color: var(--muted);
      font-weight: 400;
    }
    .excluded {
      color: var(--muted);
      font-size: var(--fs-sub);
    }
  `,
})
export class FactorTable {
  readonly factors = input.required<unknown>();
  readonly caption = input.required<string>();

  protected readonly result = computed(() => ruleResultOf(this.factors()));
  protected readonly level = computed(() => priorityOfLevel(this.result()?.level ?? 1));
  protected readonly bounds = computed(() => [
    ...(this.result()?.caps ?? []),
    ...(this.result()?.floors ?? []),
  ]);
}
