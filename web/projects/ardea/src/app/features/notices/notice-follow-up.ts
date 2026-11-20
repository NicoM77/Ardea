import {
  afterNextRender,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiButton, UiField, UiInput, UiNotice } from 'ui';
import type { ActionSummary, NoticeView } from '../../core/api/client';
import { actionName, num } from '../actions/action-format';
import { defaultPlaceId, openReminderFor, reasonComponent } from './notice-format';

export interface FollowUpAnswer {
  projectId: string | null;
  version: string | null;
}

@Component({
  selector: 'ardea-notice-follow-up',
  imports: [TranslocoPipe, UiButton, UiField, UiInput, UiNotice],
  template: `
    @let open = joins();
    <form novalidate (submit)="submit($event)" (keydown.escape)="escape($event)">
      <p class="lead" [id]="uid + '-lead'">
        @if (open) {
          {{
            (placeName() ? 'notices.follow.joinsProject' : 'notices.follow.joinsStack')
              | transloco
                : {
                    number: reminderNumber(),
                    reminder: reminderTitle(),
                    entries: openEntries(),
                    project: placeName(),
                  }
          }}
        } @else {
          {{
            (placeName() ? 'notices.follow.createsProject' : 'notices.follow.createsStack')
              | transloco: { product: product(), project: placeName() }
          }}
        }
      </p>
      <div class="fields">
        <ui-field
          [label]="'notices.follow.project' | transloco"
          [error]="projectError() ? (projectError()! | transloco) : null"
          optional
        >
          <select uiInput #project (change)="placeId.set(project.value || null)">
            <option value="">
              {{
                defaultPlace()
                  ? ('notices.follow.noProjectThere' | transloco: { project: defaultPlace() })
                  : ('notices.follow.noProject' | transloco)
              }}
            </option>
            @for (p of projects(); track p.id) {
              <option [value]="p.id">{{ p.name }}</option>
            }
          </select>
        </ui-field>
        <ui-field
          [label]="'notices.follow.version' | transloco"
          [hint]="'notices.follow.versionHint' | transloco"
          [error]="versionError() ? (versionError()! | transloco) : null"
          optional
        >
          <input
            uiInput
            mono
            #version
            autocomplete="off"
            spellcheck="false"
            maxlength="100"
            [placeholder]="'notices.follow.versionPlaceholder' | transloco"
          />
        </ui-field>
      </div>
      @if (problem(); as p) {
        <ui-notice tone="error">{{ p | transloco }}</ui-notice>
      }
      <div class="buttons">
        <button
          uiButton
          variant="primary"
          type="submit"
          [loading]="busy()"
          [attr.aria-describedby]="uid + '-lead'"
        >
          {{ (open ? 'notices.follow.add' : 'notices.follow.create') | transloco }}
        </button>
        <button uiButton variant="ghost" type="button" (click)="dismiss.emit()">
          {{ 'notices.follow.cancel' | transloco }}
        </button>
      </div>
    </form>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--space-md);
      margin-top: var(--space-lg);
    }
    .lead {
      overflow-wrap: anywhere;
    }
    .fields {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-md);
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    @media (max-width: 640px) {
      .fields {
        grid-template-columns: minmax(0, 1fr);
      }
      .buttons button {
        flex: 1;
        min-height: 44px;
      }
    }
  `,
})
export class NoticeFollowUp {
  private static next = 0;
  protected readonly uid = `notice-follow-${NoticeFollowUp.next++}`;

  readonly notice = input.required<NoticeView>();
  readonly projects = input<readonly { id: string; name: string }[]>([]);
  readonly reminders = input<readonly ActionSummary[]>([]);
  readonly busy = input(false);
  readonly problem = input<string | null>(null);
  readonly versionError = input<string | null>(null);
  readonly projectError = input<string | null>(null);

  readonly answer = output<FollowUpAnswer>();
  readonly dismiss = output();

  private readonly transloco = inject(TranslocoService);
  protected readonly placeId = signal<string | null>(null);
  private readonly projectSelect = viewChild.required<ElementRef<HTMLSelectElement>>('project');
  private readonly versionInput = viewChild.required<ElementRef<HTMLInputElement>>('version');

  protected readonly product = computed(() => reasonComponent(this.notice())?.name ?? '?');
  private readonly place = computed(() => this.placeId() ?? defaultPlaceId(this.notice()));
  protected readonly placeName = computed(() => {
    const chosen = this.placeId();
    if (chosen) return this.projects().find((p) => p.id === chosen)?.name ?? null;
    const reason = reasonComponent(this.notice());
    return reason && !reason.inStack ? (reason.project?.name ?? null) : null;
  });
  protected readonly defaultPlace = computed(() => {
    const reason = reasonComponent(this.notice());
    return reason && !reason.inStack ? (reason.project?.name ?? null) : null;
  });
  protected readonly joins = computed(() =>
    openReminderFor(this.notice(), this.place(), this.reminders()),
  );
  protected readonly reminderNumber = computed(() => num(this.joins()?.number));
  protected readonly openEntries = computed(() => num(this.joins()?.openFindings));
  protected readonly reminderTitle = computed(() => {
    const r = this.joins();
    if (!r) return '';
    const name = actionName(r);
    return this.transloco.translate(name.key, name.params);
  });

  constructor() {
    afterNextRender(() => {
      const select = this.projectSelect().nativeElement;
      select.scrollIntoView?.({ block: 'nearest' });
      select.focus();
    });
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.busy()) return;
    const version = this.versionInput().nativeElement.value.trim();
    this.answer.emit({ projectId: this.placeId(), version: version || null });
  }

  protected escape(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.dismiss.emit();
  }
}
