import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideBell, LucidePlus } from '@lucide/angular';
import { UiButton, UiCard, UiEmptyState, UiLoader, UiNotice, UiPill, UiPriority } from 'ui';
import {
  listNotificationChannelsResource,
  listNotificationRulesResource,
  listProjectsResource,
  NotificationsService,
  type NotificationRuleView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { num } from '../actions/action-format';
import { injectOrganization } from '../organizations/current-organization';
import { canManage } from '../organizations/org-format';
import { NotifyFrame } from './notify-frame';
import { NotifyIcon, type NotifyIconName } from './notify-icon';
import {
  CHANNEL_LABELS,
  CHANNELS,
  matrixRow,
  thresholdPriority,
  type Channel,
  type MatrixCell,
} from './notify-format';
import { ReminderRhythmCard } from './reminder-rhythm-card';
import { ruleRequest } from './rule-request';
import { firstValueFrom } from 'rxjs';

const CHANNEL_ICONS: Record<Channel, NotifyIconName | null> = {
  in_app: null,
  email: 'mail',
  push: 'smartphone',
  webhook: 'webhook',
};

/**
 * «Notification rules» (master plan 8.3, 11.4; task 5.8; mockup notification-rules.html): the rules as a
 * matrix of rules and channels. Each cell says when and to whom; a rule turns on and off in its row.
 */
@Component({
  selector: 'ardea-rules-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideBell,
    NotifyIcon,
    LucidePlus,
    UiButton,
    UiCard,
    UiEmptyState,
    UiLoader,
    UiNotice,
    UiPill,
    UiPriority,
    NotifyFrame,
    ReminderRhythmCard,
  ],
  template: `
    @if (organization(); as org) {
      <ardea-notify-frame [organization]="org" current="rules" [sub]="summary()">
        <a
          uiButton
          variant="primary"
          uiPageActions
          [routerLink]="'/o/' + org.slug + '/notification-rules/new'"
        >
          <svg lucidePlus aria-hidden="true"></svg>{{ 'notify.rules.new' | transloco }}
        </a>

        @if (!emailAvailable()) {
          <ui-notice class="gap" [heading]="'notify.rules.emailOff.heading' | transloco">
            {{ 'notify.rules.emailOff.text' | transloco }}
          </ui-notice>
        }
        <div class="live" role="status">
          @if (problem()) {
            <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
          }
        </div>

        @if (list(); as rules) {
          @if (rules.length) {
            <ui-card [heading]="'notify.rules.matrix' | transloco">
              <span uiCardMeta>{{ 'notify.rules.timeZone' | transloco }}</span>
              <div class="scroll">
                <table class="matrix">
                  <caption class="sr-only">
                    {{
                      'notify.rules.matrix' | transloco
                    }}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">{{ 'notify.rules.rule' | transloco }}</th>
                      @for (c of channels; track c) {
                        <th scope="col">
                          @if (icons[c]; as icon) {
                            <ardea-notify-icon [name]="icon" />
                          } @else {
                            <svg lucideBell aria-hidden="true"></svg>
                          }
                          {{ channelLabels[c] | transloco }}
                        </th>
                      }
                      <th scope="col">
                        <span class="sr-only">{{ 'notify.rules.on' | transloco }}</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (r of rows(); track r.rule.id) {
                      <tr [class.off]="!r.rule.enabled">
                        <th scope="row" class="name">
                          <a [routerLink]="'/o/' + org.slug + '/notification-rules/' + r.rule.id">{{
                            r.rule.name
                          }}</a>
                          <span class="meta">
                            <ui-priority [level]="r.priority" />
                            @if (r.priority !== 'critical') {
                              {{ 'notify.rules.andAbove' | transloco }}
                            }
                            ·
                            @if (r.rule.projectIds.length) {
                              {{ projectNames(r.rule.projectIds) }}
                            } @else {
                              {{ 'notify.rules.allProjects' | transloco }}
                            }
                            ·
                            {{ 'notify.rules.events' | transloco: { count: r.rule.events.length } }}
                          </span>
                        </th>
                        @for (c of channels; track c) {
                          <td [attr.data-label]="channelLabels[c] | transloco">
                            @if (r.cells[c]; as cell) {
                              <span class="cell">
                                @for (w of cell.when; track $index) {
                                  <b
                                    >{{ w.key | transloco }}
                                    @if (w.key === 'notify.schedule.custom') {
                                      <code> {{ w.cron }}</code>
                                    }
                                  </b>
                                }
                                <small>{{ who(cell) }}</small>
                                @if (cell.sendEmpty) {
                                  <small>{{ 'notify.rules.sendEmpty' | transloco }}</small>
                                }
                                @if (c === 'email' && !emailAvailable()) {
                                  <ui-pill>{{ 'notify.rules.emailOffPill' | transloco }}</ui-pill>
                                }
                              </span>
                            } @else {
                              <span class="none"
                                >–<span class="sr-only">{{
                                  'notify.rules.notOn' | transloco
                                }}</span></span
                              >
                            }
                          </td>
                        }
                        <td class="toggle">
                          <button
                            type="button"
                            role="switch"
                            class="switch"
                            [attr.aria-checked]="r.rule.enabled"
                            [attr.aria-label]="
                              'notify.rules.toggle' | transloco: { name: r.rule.name }
                            "
                            [disabled]="busy() === r.rule.id"
                            (click)="toggle(r.rule)"
                          ></button>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
              <p class="foot">{{ 'notify.rules.overlap' | transloco }}</p>
            </ui-card>
          } @else {
            <ui-empty-state [heading]="'notify.rules.emptyHeading' | transloco">
              <svg uiEmptyIcon lucideBell aria-hidden="true"></svg>
              {{ 'notify.rules.emptyText' | transloco }}
            </ui-empty-state>
          }
        } @else if (rules.error()) {
          <ui-notice tone="error">{{ 'notify.loadFailed' | transloco }}</ui-notice>
        } @else {
          <ui-loader [label]="'notify.loading' | transloco" />
        }
        <ardea-reminder-rhythm-card class="after" [organizationId]="org.id" />
      </ardea-notify-frame>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .after {
      margin-top: var(--gap);
    }
    .gap {
      display: block;
      margin-bottom: var(--gap);
    }
    .live:empty {
      display: none;
    }
    .live {
      margin-bottom: var(--space-lg);
    }
    .scroll {
      overflow-x: auto;
    }
    .matrix {
      width: 100%;
      border-collapse: collapse;
    }
    thead th {
      height: 32px;
      padding: 0 var(--space-md) 0 0;
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 400;
      text-align: left;
      white-space: nowrap;
    }
    thead th svg,
    thead th ardea-notify-icon {
      display: inline-flex;
      width: 14px;
      height: 14px;
      margin-right: var(--space-xs);
      vertical-align: -2px;
    }
    thead th:not(:first-child):not(:last-child) {
      width: 15%;
    }
    tbody th,
    tbody td {
      padding: var(--space-md) var(--space-md) var(--space-md) 0;
      border-top: 1px solid var(--rule);
      text-align: left;
      vertical-align: top;
      font-weight: 400;
    }
    tbody td.toggle {
      padding-right: 0;
      text-align: right;
    }
    .name a {
      display: block;
      color: var(--text);
      font-weight: 500;
    }
    .name a:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0 var(--space-xs);
      margin-top: 2px;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .cell {
      display: grid;
      gap: 2px;
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .cell b {
      font-weight: 500;
    }
    .cell small {
      color: var(--muted);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .cell ui-pill {
      justify-self: start;
      margin-top: 2px;
    }
    .none {
      color: var(--faint);
    }
    tr.off .name a,
    tr.off .cell {
      color: var(--faint);
    }
    .switch {
      position: relative;
      display: inline-flex;
      width: 40px;
      height: 24px;
      padding: 0;
      border: 0;
      border-radius: var(--radius-pill);
      background: var(--field);
      cursor: pointer;
      transition: background-color var(--t-ui) var(--ease-out);
    }
    .switch::after {
      content: '';
      position: absolute;
      top: 3px;
      left: 3px;
      width: 18px;
      height: 18px;
      border-radius: 50%;
      background: var(--surface);
      transition: transform var(--t-ui) var(--ease-out);
    }
    .switch[aria-checked='true'] {
      background: var(--primary);
    }
    .switch[aria-checked='true']::after {
      transform: translateX(16px);
    }
    .switch:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .switch:disabled {
      cursor: progress;
    }
    .foot {
      margin-top: var(--space-md);
      color: var(--faint);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    @media (max-width: 640px) {
      thead {
        display: none;
      }
      tbody tr {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: var(--space-sm) var(--space-md);
        padding: var(--space-md) 0;
        border-top: 1px solid var(--rule);
      }
      tbody th,
      tbody td {
        padding: 0;
        border: 0;
      }
      tbody td:not(.toggle) {
        grid-column: 1 / -1;
      }
      tbody td.toggle {
        grid-column: 2;
        grid-row: 1;
      }
      tbody td:not(.toggle)::before {
        content: attr(data-label);
        display: block;
        color: var(--faint);
        font-size: var(--fs-small);
      }
      tbody td:has(.none) {
        display: none;
      }
    }
  `,
})
export class RulesPage {
  private readonly api = inject(NotificationsService);
  private readonly transloco = inject(TranslocoService);
  protected readonly organization = injectOrganization().organization;
  private readonly adminOrgId = computed(() => {
    const org = this.organization();
    return org && canManage(org.role) ? org.id : undefined;
  });

  protected readonly rules = listNotificationRulesResource(this.adminOrgId);
  private readonly channelState = listNotificationChannelsResource(this.adminOrgId);
  private readonly projects = listProjectsResource(this.adminOrgId);

  protected readonly channels = CHANNELS;
  protected readonly channelLabels = CHANNEL_LABELS;
  protected readonly icons = CHANNEL_ICONS;
  protected readonly busy = signal<string | null>(null);
  protected readonly problem = signal<string | null>(null);

  protected readonly list = computed(() =>
    this.rules.hasValue() ? (this.rules.value() ?? null) : null,
  );
  protected readonly rows = computed(() =>
    (this.list() ?? []).map((rule) => ({
      rule,
      priority: thresholdPriority(rule.minLevel),
      cells: matrixRow(rule),
    })),
  );
  protected readonly emailAvailable = computed(() => {
    const channels = this.channelState.hasValue() ? this.channelState.value() : undefined;
    return channels?.find((c) => c.channel === 'email')?.available ?? true;
  });
  protected readonly summary = computed(() => {
    const rules = this.list();
    if (!rules) return null;
    return this.transloco.translate('notify.rules.summary', {
      count: rules.length,
      off: rules.filter((r) => !r.enabled).length,
    });
  });

  protected who(cell: MatrixCell): string {
    return cell.who
      .map((w) => (w.key ? this.transloco.translate(w.key) : (w.name ?? '')))
      .filter((w) => w)
      .join(', ');
  }

  protected projectNames(ids: readonly string[]): string {
    const projects = this.projects.hasValue() ? (this.projects.value() ?? []) : [];
    const names = ids.map((id) => projects.find((p) => p.id === id)?.name).filter((n) => !!n);
    return names.length
      ? names.join(', ')
      : this.transloco.translate('notify.rules.projects', { count: ids.length });
  }

  protected async toggle(rule: NotificationRuleView): Promise<void> {
    const org = this.organization();
    if (!org) return;
    this.busy.set(rule.id);
    this.problem.set(null);
    try {
      await firstValueFrom(
        this.api.replaceNotificationRule(
          org.id,
          rule.id,
          ruleRequest(rule, { enabled: !rule.enabled }),
        ),
      );
      this.rules.reload();
    } catch (error) {
      this.problem.set(problemMessageKey(error, { fallback: 'notify.rules.saveFailed' }));
    } finally {
      this.busy.set(null);
    }
  }

  protected readonly num = num;
}
