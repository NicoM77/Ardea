import { Component, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucidePause, LucidePlay, LucideRefreshCw } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiMenu, UiMenuItem, UiNotice, UiOverflowMenu, UiToaster } from 'ui';
import { SourcesService, type SourceStatus } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';

export const SOURCE_PROBLEMS = {
  codes: { source_disabled: 'sources.admin.disabled', not_found: 'sources.admin.gone' },
  fallback: 'sources.admin.failed',
};

@Component({
  selector: 'ardea-source-controls',
  imports: [
    TranslocoPipe,
    LucidePause,
    LucidePlay,
    LucideRefreshCw,
    UiButton,
    UiMenu,
    UiMenuItem,
    UiNotice,
    UiOverflowMenu,
  ],
  template: `
    <div class="buttons">
      @if (source().enabled) {
        <ui-overflow-menu [name]="source().name" [busy]="busy()">
          <ng-template>
            <div uiMenu>
              <button uiMenuItem type="button" (triggered)="syncNow()">
                <svg lucideRefreshCw aria-hidden="true"></svg>
                {{ 'sources.admin.syncNow' | transloco }}
              </button>
              <button uiMenuItem type="button" (triggered)="setEnabled(false)">
                <svg lucidePause aria-hidden="true"></svg>
                {{ 'sources.admin.pause' | transloco }}
              </button>
            </div>
          </ng-template>
        </ui-overflow-menu>
      } @else {
        <button
          uiButton
          type="button"
          [loading]="busy()"
          [attr.aria-label]="'sources.admin.resumeNamed' | transloco: { name: source().name }"
          (click)="setEnabled(true)"
        >
          <svg lucidePlay aria-hidden="true"></svg>
          {{ 'sources.admin.resume' | transloco }}
        </button>
      }
    </div>
    @if (problem()) {
      <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
    }
  `,
  styles: `
    :host {
      display: grid;
      justify-items: end;
      gap: var(--space-sm);
    }
    .buttons {
      display: flex;
      gap: var(--space-sm);
    }
    @media (max-width: 640px) {
      :host {
        justify-items: start;
      }
    }
  `,
})
export class SourceControls {
  readonly source = input.required<SourceStatus>();
  readonly changed = output();

  private readonly api = inject(SourcesService);
  private readonly toaster = inject(UiToaster);
  private readonly transloco = inject(TranslocoService);

  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);

  protected async syncNow(): Promise<void> {
    const source = this.source();
    if (await this.run(() => firstValueFrom(this.api.syncSource(source.key))))
      this.toaster.show({ text: this.transloco.translate('sources.admin.syncRequested') });
  }

  protected async setEnabled(enabled: boolean): Promise<void> {
    const key = this.source().key;
    const update = (on: boolean) =>
      this.run(() =>
        firstValueFrom(this.api.updateSource(key, { enabled: on, intervalMinutes: null })),
      );
    if (await update(enabled))
      this.toaster.show({
        text: this.transloco.translate(enabled ? 'sources.admin.resumed' : 'sources.admin.paused'),
        undo: () => void update(!enabled),
      });
  }

  private async run(work: () => Promise<unknown>): Promise<boolean> {
    this.busy.set(true);
    this.problem.set(null);
    try {
      await work();
      this.changed.emit();
      return true;
    } catch (error) {
      this.problem.set(problemMessageKey(error, SOURCE_PROBLEMS));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
