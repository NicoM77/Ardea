import { Component, computed, inject, input } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideDownload } from '@lucide/angular';
import { UiButton, UiCopyButton } from 'ui';

/** The recovery codes, shown once: a numbered grid in mono, to copy or download as a text file. */
@Component({
  selector: 'ardea-recovery-codes',
  imports: [TranslocoPipe, LucideDownload, UiButton, UiCopyButton],
  template: `
    <ol class="codes" [attr.aria-label]="'account.twoFactor.codes.listLabel' | transloco">
      @for (code of codes(); track code) {
        <li>
          <span class="id">{{ code }}</span>
        </li>
      }
    </ol>
    <div class="tools">
      <ui-copy-button [text]="asText()" [label]="'account.twoFactor.codes.copy' | transloco" />
      <button uiButton type="button" (click)="download()">
        <svg lucideDownload aria-hidden="true"></svg>
        {{ 'account.twoFactor.codes.download' | transloco }}
      </button>
    </div>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-lg);
    }
    /* Two columns where a whole code and its number fit in each, one column otherwise. The list is
       set in mono, so ch is the width of one character of a code. */
    .codes {
      display: grid;
      grid-template-columns: repeat(
        auto-fill,
        minmax(min(100%, max(20ch, calc((100% - var(--gap)) / 2))), 1fr)
      );
      gap: var(--space-sm) var(--gap);
      padding: var(--space-lg) var(--space-xl);
      border-radius: var(--radius-card);
      background: var(--raised);
      font-family: var(--mono);
      font-size: var(--fs-body);
      line-height: var(--lh-body);
      counter-reset: code;
    }
    /* The number has its own track, so it can never run into a code */
    .codes li {
      display: grid;
      grid-template-columns: 3ch minmax(0, 1fr);
      column-gap: var(--space-sm);
      align-items: baseline;
      min-width: 0;
      counter-increment: code;
    }
    .codes li::before {
      content: counter(code);
      color: var(--faint);
      font: 400 var(--fs-small) / var(--lh-small) var(--font);
      text-align: right;
    }
    .codes .id {
      font-size: inherit;
      letter-spacing: 0.04em;
      white-space: normal;
      overflow-wrap: anywhere;
    }
    .tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    @media (max-width: 640px) {
      .codes {
        padding: var(--space-md) var(--space-lg);
      }
    }
  `,
})
export class RecoveryCodes {
  readonly codes = input.required<readonly string[]>();
  private readonly transloco = inject(TranslocoService);

  protected readonly asText = computed(() => this.codes().join('\n'));

  protected download(): void {
    const header = this.transloco.translate('account.twoFactor.codes.fileHeader');
    const blob = new Blob([`${header}\n\n${this.asText()}\n`], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'ardea-recovery-codes.txt';
    link.click();
    URL.revokeObjectURL(url);
  }
}
