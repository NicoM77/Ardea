import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  EnableTwoFactorRequest,
  EnableTwoFactorResponse,
  TwoFactorSetup,
  TwoFactorSetupRequest,
} from './models';

/**
 * Writes of the signed-in user's own account. The account itself is read through `Session` (`GET /api/v1/auth/session`).
 * Thin on purpose: task 0.11 replaces it with the generated client.
 */
@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);

  /** The password is required again (master plan 9.3: a stolen session must not change the second factor). */
  startTwoFactorSetup(body: TwoFactorSetupRequest): Promise<TwoFactorSetup> {
    return firstValueFrom(this.http.post<TwoFactorSetup>('/api/v1/account/mfa/setup', body));
  }

  enableTwoFactor(body: EnableTwoFactorRequest): Promise<EnableTwoFactorResponse> {
    return firstValueFrom(this.http.post<EnableTwoFactorResponse>('/api/v1/account/mfa', body));
  }
}
