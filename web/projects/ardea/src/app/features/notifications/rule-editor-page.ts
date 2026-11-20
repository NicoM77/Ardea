import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucidePlus } from '@lucide/angular';
import {
  UiBackLink,
  UiButton,
  UiCard,
  UiInput,
  UiLoader,
  UiMenu,
  UiMenuItem,
  UiNotice,
  UiOverflowMenu,
  UiPriority,
  UiToaster,
} from 'ui';
import { firstValueFrom } from 'rxjs';
import {
  getNotificationRuleResource,
  listExternalAddressesResource,
  listMembersResource,
  listProjectsResource,
  listWebhookEndpointsResource,
  NotificationsService,
  type NotificationRuleRequest,
  type NotificationRuleView,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { num } from '../actions/action-format';
import { injectOrganization } from '../organizations/current-organization';
import { canManage } from '../organizations/org-format';
import {
  CHANNEL_LABELS,
  channelsFor,
  DEFAULT_EVENTS,
  EVENT_GROUPS,
  ROLE_TARGET_LABELS,
  ROLE_TARGETS,
  SCHEDULES,
  scheduleLabel,
  THRESHOLDS,
  type Channel,
} from './notify-format';
import { NotifyIcon } from './notify-icon';

interface TargetDraft {
  recipient: string;
  channel: Channel;
  mode: 'immediate' | 'digest';
  schedule: string;
  cron: string;
  sendEmpty: boolean;
}

const PRIORITY_WORDS: Readonly<Record<string, string>> = {
  critical: 'ui.priority.critical',
  high: 'ui.priority.high',
  medium: 'ui.priority.medium',
  low: 'ui.priority.low',
};

const VIA: Readonly<Record<string, string>> = {
  in_app: 'notify.editor.via.inApp',
  email: 'notify.editor.via.email',
  push: 'notify.editor.via.push',
  webhook: 'notify.editor.via.webhook',
};

function newTarget(): TargetDraft {
  return {
    recipient: 'role:admin',
    channel: 'in_app',
    mode: 'immediate',
    schedule: SCHEDULES[0].cron,
    cron: SCHEDULES[0].cron,
    sendEmpty: false,
  };
}

@Component({
  selector: 'ardea-rule-editor-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucidePlus,
    NotifyIcon,
    UiBackLink,
    UiButton,
    UiCard,
    UiInput,
    UiLoader,
    UiMenu,
    UiMenuItem,
    UiNotice,
    UiOverflowMenu,
    UiPriority,
  ],
  template: `
    @if (organization(); as org) {
      <ui-back-link [label]="'nav.rules' | transloco" [link]="listUrl()" />
      <div class="title">
        <h1>{{ title() }}</h1>
        @if (manage() && ruleId() && rule.hasValue()) {
          <ui-overflow-menu [name]="title()" [busy]="deleting()">
            <ng-template>
              <div uiMenu>
                <button uiMenuItem destructive type="button" (triggered)="remove()">
                  {{ 'notify.editor.delete' | transloco }}
                </button>
              </div>
            </ng-template>
          </ui-overflow-menu>
        }
      </div>
      @if (rule.value()?.preset) {
        <p class="lead">{{ 'notify.editor.preset' | transloco }}</p>
      }

      @if (!manage()) {
        <ui-notice>{{ 'notify.adminsOnly.text' | transloco }}</ui-notice>
      } @else if (ruleId() && !rule.hasValue()) {
        @if (rule.error()) {
          <ui-notice tone="error">{{ 'notify.editor.notFound' | transloco }}</ui-notice>
        } @else {
          <ui-loader [label]="'notify.loading' | transloco" />
        }
      } @else {
        <form class="editor" novalidate (submit)="save($event)">
          <div class="main">
            <ui-card [heading]="'notify.editor.when' | transloco">
              <div class="fields">
                <div class="field">
                  <label class="label" for="rule-name">{{
                    'notify.editor.name' | transloco
                  }}</label>
                  <input
                    uiInput
                    id="rule-name"
                    type="text"
                    maxlength="200"
                    [value]="name()"
                    [attr.aria-invalid]="errors()['name'] ? true : null"
                    [attr.aria-describedby]="errors()['name'] ? 'rule-name-error' : null"
                    (input)="name.set($any($event.target).value)"
                  />
                  @if (errors()['name']) {
                    <p class="error" id="rule-name-error">{{ errors()['name'] | transloco }}</p>
                  }
                </div>

                <div class="field">
                  <span class="label" id="rule-level">{{ 'notify.editor.level' | transloco }}</span>
                  <div class="levels" role="group" aria-labelledby="rule-level">
                    @for (t of thresholds; track t.level) {
                      <button
                        type="button"
                        [attr.aria-pressed]="minLevel() === t.level"
                        (click)="minLevel.set(t.level)"
                      >
                        <ui-priority [level]="t.priority" />
                      </button>
                    }
                  </div>
                  <p class="hint">{{ 'notify.editor.levelHint' | transloco }}</p>
                </div>

                <fieldset [attr.aria-describedby]="errors()['events'] ? 'rule-events-error' : null">
                  <legend>{{ 'notify.editor.events' | transloco }}</legend>
                  @if (errors()['events']) {
                    <p class="error" id="rule-events-error">{{ errors()['events'] | transloco }}</p>
                  }
                  <div class="checks">
                    @for (group of eventGroups; track group.label) {
                      <span class="group">{{ group.label | transloco }}</span>
                      @for (e of group.events; track e.value) {
                        <label class="check">
                          <input
                            type="checkbox"
                            [checked]="events().includes(e.value)"
                            (change)="toggleEvent(e.value)"
                          />
                          <span
                            >{{ e.label | transloco }}
                            @if (e.hint) {
                              <small>{{ e.hint | transloco }}</small>
                            }
                          </span>
                        </label>
                      }
                    }
                  </div>
                </fieldset>

                <fieldset>
                  <legend>{{ 'notify.editor.projects' | transloco }}</legend>
                  <div class="checks">
                    <label class="check">
                      <input
                        type="radio"
                        name="scope"
                        [checked]="allProjects()"
                        (change)="allProjects.set(true)"
                      />
                      <span
                        >{{ 'notify.rules.allProjects' | transloco }}
                        <small>{{ 'notify.editor.allProjectsHint' | transloco }}</small></span
                      >
                    </label>
                    <label class="check">
                      <input
                        type="radio"
                        name="scope"
                        [checked]="!allProjects()"
                        (change)="allProjects.set(false)"
                      />
                      <span>{{ 'notify.editor.selectedProjects' | transloco }}</span>
                    </label>
                    @if (!allProjects()) {
                      @for (p of projectList(); track p.id) {
                        <label class="check">
                          <input
                            type="checkbox"
                            [checked]="projectIds().includes(p.id)"
                            (change)="toggleProject(p.id)"
                          />
                          <span>{{ p.name }}</span>
                        </label>
                      }
                    }
                  </div>
                </fieldset>
              </div>
            </ui-card>

            <ui-card [heading]="'notify.editor.who' | transloco">
              <span uiCardMeta>{{
                'notify.editor.targetCount' | transloco: { count: targets().length }
              }}</span>
              @if (errors()['targets']) {
                <p class="error">{{ errors()['targets'] | transloco }}</p>
              }
              <ol class="targets">
                @for (t of targets(); track $index; let i = $index) {
                  <li class="target">
                    <div class="field">
                      <label class="label" [for]="'t-who-' + i">{{
                        'notify.editor.recipient' | transloco
                      }}</label>
                      <select
                        uiInput
                        [id]="'t-who-' + i"
                        (change)="setRecipient(i, $any($event.target).value)"
                      >
                        <optgroup [label]="'notify.editor.roles' | transloco">
                          @for (r of roles; track r.value) {
                            <option
                              [value]="'role:' + r.value"
                              [selected]="t.recipient === 'role:' + r.value"
                            >
                              {{ r.label | transloco }}
                            </option>
                          }
                        </optgroup>
                        @if (memberOptions().length) {
                          <optgroup [label]="'notify.editor.members' | transloco">
                            @for (m of memberOptions(); track m.value) {
                              <option [value]="m.value" [selected]="t.recipient === m.value">
                                {{ m.label }}
                              </option>
                            }
                          </optgroup>
                        }
                        @if (addressOptions().length) {
                          <optgroup [label]="'notify.editor.addresses' | transloco">
                            @for (a of addressOptions(); track a.value) {
                              <option [value]="a.value" [selected]="t.recipient === a.value">
                                {{ a.label }}
                              </option>
                            }
                          </optgroup>
                        }
                        @if (endpointOptions().length) {
                          <optgroup [label]="'notify.editor.webhooks' | transloco">
                            @for (e of endpointOptions(); track e.value) {
                              <option [value]="e.value" [selected]="t.recipient === e.value">
                                {{ e.label }}
                              </option>
                            }
                          </optgroup>
                        }
                      </select>
                    </div>
                    <div class="field">
                      <label class="label" [for]="'t-channel-' + i">{{
                        'notify.editor.channel' | transloco
                      }}</label>
                      <select
                        uiInput
                        [id]="'t-channel-' + i"
                        (change)="patch(i, { channel: $any($event.target).value })"
                      >
                        @for (c of channelsOf(t); track c) {
                          <option [value]="c" [selected]="t.channel === c">
                            {{ channelLabels[c] | transloco }}
                          </option>
                        }
                      </select>
                    </div>
                    <div class="field">
                      <label class="label" [for]="'t-mode-' + i">{{
                        'notify.editor.mode' | transloco
                      }}</label>
                      <select
                        uiInput
                        [id]="'t-mode-' + i"
                        (change)="patch(i, { mode: $any($event.target).value })"
                      >
                        <option value="immediate" [selected]="t.mode === 'immediate'">
                          {{ 'notify.mode.immediate' | transloco }}
                        </option>
                        @if (t.channel !== 'push') {
                          <option value="digest" [selected]="t.mode === 'digest'">
                            {{ 'notify.mode.digest' | transloco }}
                          </option>
                        }
                      </select>
                    </div>
                    <button
                      uiButton
                      variant="ghost"
                      iconOnly
                      type="button"
                      class="remove"
                      [attr.aria-label]="'notify.editor.remove' | transloco: { number: i + 1 }"
                      (click)="removeTarget(i)"
                    >
                      <ardea-notify-icon name="trash" />
                    </button>
                    @if (t.mode === 'digest') {
                      <div class="extra">
                        <label>
                          <span>{{ 'notify.editor.schedule' | transloco }}</span>
                          <select uiInput (change)="setSchedule(i, $any($event.target).value)">
                            @for (s of schedules; track s.cron) {
                              <option [value]="s.cron" [selected]="t.schedule === s.cron">
                                {{ s.label | transloco }}
                              </option>
                            }
                            <option value="custom" [selected]="t.schedule === 'custom'">
                              {{ 'notify.schedule.custom' | transloco }}
                            </option>
                          </select>
                        </label>
                        @if (t.schedule === 'custom') {
                          <label>
                            <span>{{ 'notify.editor.cron' | transloco }}</span>
                            <input
                              uiInput
                              mono
                              type="text"
                              [value]="t.cron"
                              (input)="patch(i, { cron: $any($event.target).value })"
                            />
                          </label>
                        }
                        <label class="inline">
                          <input
                            type="checkbox"
                            [checked]="t.sendEmpty"
                            (change)="patch(i, { sendEmpty: !t.sendEmpty })"
                          />
                          {{ 'notify.editor.sendEmpty' | transloco }}
                        </label>
                      </div>
                    }
                    @if (errors()['targets[' + i + ']']) {
                      <p class="error wide">{{ errors()['targets[' + i + ']'] | transloco }}</p>
                    }
                  </li>
                }
              </ol>
              @if (targets().length < 20) {
                <div>
                  <button uiButton type="button" (click)="addTarget()">
                    <svg lucidePlus aria-hidden="true"></svg
                    >{{ 'notify.editor.addTarget' | transloco }}
                  </button>
                </div>
              }
            </ui-card>
          </div>

          <aside class="side">
            <ui-card [heading]="'notify.editor.summary' | transloco">
              <p class="sentence">{{ sentence() }}</p>
              <div class="live" role="status">
                @if (problem()) {
                  <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
                }
              </div>
              <div class="actions">
                <button uiButton variant="primary" type="submit" [loading]="saving()">
                  {{ 'notify.editor.save' | transloco }}
                </button>
                <a uiButton [routerLink]="listUrl()">{{ 'notify.editor.cancel' | transloco }}</a>
              </div>
            </ui-card>
          </aside>
        </form>
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .title {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--space-md);
      margin-bottom: var(--space-xs);
    }
    .lead {
      color: var(--muted);
    }
    .editor {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      gap: var(--gap);
      align-items: start;
      margin-top: var(--space-xl);
    }
    .main {
      grid-column: span 8;
      display: grid;
      gap: var(--gap);
      min-width: 0;
    }
    .side {
      grid-column: span 4;
      position: sticky;
      top: calc(var(--topbar) + var(--space-lg));
    }
    .fields {
      display: grid;
      gap: var(--space-xl);
    }
    .field {
      display: grid;
      gap: 6px;
      min-width: 0;
    }
    .label,
    legend {
      font-weight: 500;
    }
    legend {
      margin-bottom: var(--space-sm);
      padding: 0;
    }
    fieldset {
      min-width: 0;
      margin: 0;
      padding: 0;
      border: 0;
    }
    .hint {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .error {
      color: var(--text);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .levels {
      display: inline-flex;
      flex-wrap: wrap;
      gap: 2px;
      justify-self: start;
      padding: var(--space-xs);
      border-radius: var(--radius-card);
      background: var(--raised);
    }
    .levels button {
      display: inline-flex;
      align-items: center;
      height: 32px;
      padding: 0 var(--space-md);
      border: 0;
      border-radius: var(--radius-control);
      background: none;
      cursor: pointer;
    }
    .levels button[aria-pressed='true'] {
      background: var(--surface);
      box-shadow: inset 0 0 0 1px var(--border);
    }
    .levels button:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .checks {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-xs) var(--gap);
    }
    .group {
      grid-column: 1 / -1;
      margin-top: var(--space-sm);
      color: var(--faint);
      font-size: var(--fs-small);
    }
    .group:first-child {
      margin-top: 0;
    }
    .check {
      display: grid;
      grid-template-columns: 16px minmax(0, 1fr);
      align-items: start;
      gap: var(--space-sm);
      min-height: 32px;
      padding: 6px 0;
      cursor: pointer;
    }
    .check input {
      width: 16px;
      height: 16px;
      margin: 2px 0 0;
      accent-color: var(--primary);
    }
    .check small {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .targets {
      display: grid;
      margin-bottom: var(--space-md);
    }
    .target {
      display: grid;
      grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr) minmax(0, 1fr) 32px;
      align-items: end;
      gap: var(--space-md);
      padding: var(--space-lg) 0;
    }
    .target:first-child {
      padding-top: var(--space-xs);
    }
    .target + .target {
      border-top: 1px solid var(--rule);
    }
    .target .label {
      color: var(--muted);
      font-size: var(--fs-sub);
      font-weight: 400;
    }
    .extra {
      grid-column: 1 / -1;
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-sm) var(--gap);
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    .extra label {
      display: inline-flex;
      align-items: center;
      gap: var(--space-sm);
    }
    .extra input[type='checkbox'] {
      accent-color: var(--primary);
    }
    .wide {
      grid-column: 1 / -1;
    }
    .sentence {
      color: var(--muted);
      line-height: var(--lh-body);
    }
    .live:empty {
      display: none;
    }
    .live {
      margin-top: var(--space-lg);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
      margin-top: var(--space-lg);
    }
    @media (max-width: 1100px) {
      .main,
      .side {
        grid-column: span 12;
        position: static;
      }
    }
    @media (max-width: 640px) {
      .checks {
        grid-template-columns: minmax(0, 1fr);
      }
      .target {
        grid-template-columns: minmax(0, 1fr) 40px;
      }
      .target .field {
        grid-column: 1;
      }
      .target .remove {
        grid-column: 2;
        grid-row: 1;
      }
    }
  `,
})
export class RuleEditorPage {
  readonly ruleId = input<string | undefined>(undefined);

  private readonly api = inject(NotificationsService);
  private readonly router = inject(Router);
  private readonly toaster = inject(UiToaster);
  private readonly transloco = inject(TranslocoService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly organization = injectOrganization().organization;
  protected readonly manage = computed(() => canManage(this.organization()?.role));
  private readonly orgId = computed(() => (this.manage() ? this.organization()?.id : undefined));
  private readonly orgAndRule = computed(() => this.ruleId());

  protected readonly rule = getNotificationRuleResource(this.orgId, this.orgAndRule);
  private readonly members = listMembersResource(this.orgId);
  private readonly addresses = listExternalAddressesResource(this.orgId);
  private readonly endpoints = listWebhookEndpointsResource(this.orgId);
  private readonly projects = listProjectsResource(this.orgId);

  protected readonly thresholds = THRESHOLDS;
  protected readonly eventGroups = EVENT_GROUPS;
  protected readonly roles = ROLE_TARGETS;
  protected readonly schedules = SCHEDULES;
  protected readonly channelLabels = CHANNEL_LABELS;

  protected readonly name = signal('');
  protected readonly minLevel = signal(4);
  protected readonly events = signal<string[]>([...DEFAULT_EVENTS]);
  protected readonly allProjects = signal(true);
  protected readonly projectIds = signal<string[]>([]);
  protected readonly targets = signal<TargetDraft[]>([newTarget()]);
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly problem = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly deleting = signal(false);

  protected readonly listUrl = computed(() => `/o/${this.organization()?.slug}/notification-rules`);
  protected readonly title = computed(() =>
    this.ruleId()
      ? (this.rule.value()?.name ?? this.transloco.translate('notify.editor.titleEdit'))
      : this.transloco.translate('notify.editor.titleNew'),
  );

  protected readonly projectList = computed(() =>
    this.projects.hasValue() ? (this.projects.value() ?? []).filter((p) => !p.archived) : [],
  );
  protected readonly memberOptions = computed(() =>
    (this.members.hasValue() ? (this.members.value() ?? []) : []).map((m) => ({
      value: `user:${m.userId}`,
      label: m.displayName,
    })),
  );
  protected readonly addressOptions = computed(() =>
    (this.addresses.hasValue() ? (this.addresses.value() ?? []) : []).map((a) => ({
      value: `address:${a.id}`,
      label: a.address,
    })),
  );
  protected readonly endpointOptions = computed(() =>
    (this.endpoints.hasValue() ? (this.endpoints.value() ?? []) : []).map((e) => ({
      value: `endpoint:${e.id}`,
      label: e.name,
    })),
  );

  protected readonly sentence = computed(() => {
    const t = (key: string, params?: Record<string, unknown>) =>
      this.transloco.translate(key, params);
    const priority = THRESHOLDS.find((x) => x.level === this.minLevel())?.priority ?? 'high';
    const level = t(PRIORITY_WORDS[priority]);
    const projects = this.allProjects()
      ? t('notify.editor.sentenceAll')
      : t('notify.rules.projects', { count: this.projectIds().length });
    const parts = this.targets().map((target) =>
      t('notify.editor.sentenceTarget', {
        who: this.recipientLabel(target.recipient),
        via: t(VIA[target.channel]),
        when:
          target.mode === 'digest'
            ? scheduleLabel(target.schedule === 'custom' ? target.cron : target.schedule)
              ? t(scheduleLabel(target.schedule === 'custom' ? target.cron : target.schedule)!)
              : target.cron
            : t('notify.editor.atOnce'),
      }),
    );
    return t(this.minLevel() === 5 ? 'notify.editor.sentenceTop' : 'notify.editor.sentence', {
      level,
      projects,
      targets: parts.join('; '),
    });
  });

  constructor() {
    effect(() => {
      const rule = this.rule.hasValue() ? this.rule.value() : undefined;
      if (rule) untracked(() => this.fill(rule));
    });
  }

  protected toggleEvent(value: string): void {
    this.events.update((list) =>
      list.includes(value) ? list.filter((e) => e !== value) : [...list, value],
    );
  }

  protected toggleProject(id: string): void {
    this.projectIds.update((list) =>
      list.includes(id) ? list.filter((p) => p !== id) : [...list, id],
    );
  }

  protected channelsOf(target: TargetDraft): Channel[] {
    return channelsFor(target.recipient.split(':')[0]);
  }

  protected setRecipient(index: number, recipient: string): void {
    const allowed = channelsFor(recipient.split(':')[0]);
    const current = this.targets()[index];
    this.patch(index, {
      recipient,
      channel: allowed.includes(current.channel) ? current.channel : allowed[0],
    });
  }

  protected setSchedule(index: number, schedule: string): void {
    this.patch(index, schedule === 'custom' ? { schedule } : { schedule, cron: schedule });
  }

  protected patch(index: number, change: Partial<TargetDraft>): void {
    this.targets.update((list) =>
      list.map((t, i) => {
        if (i !== index) return t;
        const next = { ...t, ...change };
        if (next.channel === 'push') next.mode = 'immediate';
        return next;
      }),
    );
  }

  protected addTarget(): void {
    this.targets.update((list) => [...list, newTarget()]);
  }

  protected removeTarget(index: number): void {
    this.targets.update((list) => list.filter((_, i) => i !== index));
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    const org = this.organization();
    if (!org || this.saving()) return;
    this.problem.set(null);
    const errors = this.validate();
    this.errors.set(errors);
    if (Object.keys(errors).length) {
      this.focusFirstError();
      return;
    }
    this.saving.set(true);
    try {
      const id = this.ruleId();
      const request = this.request();
      if (id) await firstValueFrom(this.api.replaceNotificationRule(org.id, id, request));
      else await firstValueFrom(this.api.createNotificationRule(org.id, request));
      this.toaster.show({
        text: this.transloco.translate('notify.editor.saved', { name: request.name }),
      });
      await this.router.navigateByUrl(this.listUrl());
    } catch (error) {
      const fields = problemOf(error)?.errors ?? {};
      const mapped: Record<string, string> = {};
      for (const key of Object.keys(fields)) {
        if (key === 'name') mapped[key] = 'notify.editor.errors.name';
        else if (key === 'events') mapped[key] = 'notify.editor.errors.events';
        else if (key === 'targets') mapped[key] = 'notify.editor.errors.targets';
        else if (key.startsWith('targets[')) mapped[key] = 'notify.editor.errors.target';
      }
      this.errors.set(mapped);
      this.problem.set(
        problemMessageKey(error, {
          codes: { invalid_rule: 'notify.editor.errors.invalid' },
          fallback: 'notify.rules.saveFailed',
        }),
      );
    } finally {
      this.saving.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const org = this.organization();
    const id = this.ruleId();
    const rule = this.rule.value();
    if (!org || !id || !rule) return;
    this.deleting.set(true);
    this.problem.set(null);
    try {
      await firstValueFrom(this.api.deleteNotificationRule(org.id, id));
      const again = this.request();
      this.toaster.show({
        text: this.transloco.translate('notify.editor.deleted', { name: rule.name }),
        undo: () =>
          void firstValueFrom(this.api.createNotificationRule(org.id, again)).then(() =>
            this.reloadList(),
          ),
      });
      await this.router.navigateByUrl(this.listUrl());
    } catch (error) {
      this.problem.set(problemMessageKey(error, { fallback: 'notify.rules.saveFailed' }));
    } finally {
      this.deleting.set(false);
    }
  }

  private reloadList(): void {
    if (this.router.url === this.listUrl())
      void this.router.navigateByUrl(this.listUrl(), { onSameUrlNavigation: 'reload' });
  }

  private fill(rule: NotificationRuleView): void {
    this.name.set(rule.name);
    this.minLevel.set(num(rule.minLevel));
    this.events.set([...rule.events]);
    this.allProjects.set(rule.projectIds.length === 0);
    this.projectIds.set([...rule.projectIds]);
    this.targets.set(
      rule.targets.map((t) => {
        const cron = t.digestCron ?? SCHEDULES[0].cron;
        return {
          recipient: `${t.recipientKind}:${t.recipientRef}`,
          channel: t.channel as Channel,
          mode: t.mode === 'digest' ? 'digest' : 'immediate',
          schedule: SCHEDULES.some((s) => s.cron === cron) ? cron : 'custom',
          cron,
          sendEmpty: t.sendEmpty,
        };
      }),
    );
  }

  private request(): NotificationRuleRequest {
    return {
      name: this.name().trim(),
      enabled: this.rule.value()?.enabled ?? true,
      projectIds: this.allProjects() ? [] : [...this.projectIds()],
      minLevel: this.minLevel(),
      events: [...this.events()],
      targets: this.targets().map((t) => {
        const [kind, ...ref] = t.recipient.split(':');
        return {
          channel: t.channel,
          recipientKind: kind,
          recipientRef: ref.join(':'),
          mode: t.mode,
          digestCron: t.mode === 'digest' ? t.cron.trim() : null,
          sendEmpty: t.mode === 'digest' ? t.sendEmpty : false,
        };
      }),
    };
  }

  private validate(): Record<string, string> {
    const errors: Record<string, string> = {};
    if (!this.name().trim()) errors['name'] = 'notify.editor.errors.name';
    if (!this.events().length) errors['events'] = 'notify.editor.errors.events';
    if (!this.targets().length) errors['targets'] = 'notify.editor.errors.targets';
    this.targets().forEach((t, i) => {
      if (t.mode === 'digest' && t.cron.trim().split(/\s+/).length !== 5)
        errors[`targets[${i}]`] = 'notify.editor.errors.cron';
    });
    return errors;
  }

  private recipientLabel(recipient: string): string {
    const [kind, ...rest] = recipient.split(':');
    const ref = rest.join(':');
    if (kind === 'role') return this.transloco.translate(ROLE_TARGET_LABELS[ref] ?? ref);
    const all = [...this.memberOptions(), ...this.addressOptions(), ...this.endpointOptions()];
    return all.find((o) => o.value === recipient)?.label ?? '';
  }

  private focusFirstError(): void {
    setTimeout(() =>
      this.host
        .querySelector<HTMLElement>('[aria-invalid="true"], .error')
        ?.scrollIntoView({ block: 'center' }),
    );
  }
}
