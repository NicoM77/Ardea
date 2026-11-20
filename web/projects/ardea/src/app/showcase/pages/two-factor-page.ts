import { Component } from '@angular/core';
import { UiCard, UiCopyButton, UiPageHeader, UiQrCode } from 'ui';
import { RecoveryCodes } from '../../features/account/recovery-codes';
import { formatSharedKey } from '../../features/auth/auth-forms';

const SAMPLE_KEY = 'GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ';

@Component({
  selector: 'ardea-two-factor-showcase-page',
  imports: [RecoveryCodes, UiCard, UiCopyButton, UiPageHeader, UiQrCode],
  template: `
    <div class="sc-page">
      <ui-page-header
        heading="Two-factor"
        sub="The pieces of turning on two-factor authentication, with sample values."
      />

      <div class="sc-grid">
        <ui-card heading="QR code and shared key">
          <div class="sc-stack">
            <ui-qr-code [value]="uri" label="QR code for your authenticator app" />
            <p class="id key">{{ key }}</p>
            <div><ui-copy-button [text]="secret" label="Copy key" /></div>
          </div>
        </ui-card>

        <ui-card heading="Recovery codes, phone width">
          <div class="phone"><ardea-recovery-codes [codes]="codes" /></div>
        </ui-card>
      </div>

      <ui-card class="page-width" heading="Recovery codes, page width">
        <ardea-recovery-codes [codes]="codes" />
      </ui-card>
    </div>
  `,
  styles: `
    .phone {
      width: 326px;
      max-width: 100%;
    }
    .page-width {
      max-width: 640px;
    }
    .key {
      font-size: var(--fs-section);
      letter-spacing: 0.06em;
      white-space: normal;
    }
  `,
})
export class TwoFactorShowcasePage {
  protected readonly secret = SAMPLE_KEY;
  protected readonly key = formatSharedKey(SAMPLE_KEY);
  protected readonly uri = `otpauth://totp/Ardea:sample%40example.org?secret=${SAMPLE_KEY}&issuer=Ardea&digits=6`;
  protected readonly codes = [
    'SAMPL-E0001',
    'SAMPL-E0002',
    'SAMPL-E0003',
    'SAMPL-E0004',
    'SAMPL-E0005',
    'SAMPL-E0006',
    'SAMPL-E0007',
    'SAMPL-E0008',
    'SAMPL-E0009',
    'SAMPL-E0010',
  ];
}
