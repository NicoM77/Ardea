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
    .codes {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-xs) var(--gap);
      padding: var(--space-lg) var(--space-xl);
      border-radius: var(--radius-card);
      background: var(--raised);
      counter-reset: code;
    }
    .codes li {
      display: flex;
      align-items: baseline;
      gap: var(--space-md);
      counter-increment: code;
    }
    .codes li::before {
      content: counter(code);
      min-width: 2ch;
      color: var(--faint);
      font-size: var(--fs-small);
      text-align: right;
    }
    .codes .id {
      font-size: var(--fs-body);
      letter-spacing: 0.04em;
    }
    .tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    @media (max-width: 400px) {
      .codes {
        grid-template-columns: minmax(0, 1fr);
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
