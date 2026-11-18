import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EnableTwoFactorRequest, EnableTwoFactorResponse, TwoFactorSetup } from './models';

/**
 * Writes of the signed-in user's own account. The account itself is read through `Session.me`.
 * Thin on purpose: task 0.11 replaces it with the generated client.
 */
@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);

  startTwoFactorSetup(): Promise<TwoFactorSetup> {
    return firstValueFrom(this.http.post<TwoFactorSetup>('/api/v1/me/two-factor/setup', null));
  }

  enableTwoFactor(body: EnableTwoFactorRequest): Promise<EnableTwoFactorResponse> {
    return firstValueFrom(
      this.http.post<EnableTwoFactorResponse>('/api/v1/me/two-factor/enable', body),
    );
  }
}
