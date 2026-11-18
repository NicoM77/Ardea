import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ForgotPasswordRequest,
  LoginRequest,
  LoginResponse,
  RegisterRequest,
  ResetPasswordRequest,
  SetupRequest,
  TwoFactorLoginRequest,
} from './models';

/**
 * Writes of the sign-in area: setup, sign-in, two-factor step, registration, password reset.
 * Thin on purpose: task 0.11 replaces it with the client generated from the OpenAPI document.
 */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  /** Refreshes the antiforgery cookie. Its token is bound to the user, so call it after every sign-in and sign-out. */
  antiforgery(): Promise<void> {
    return firstValueFrom(this.http.get<void>('/api/v1/auth/antiforgery'));
  }

  setup(body: SetupRequest): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/setup', body));
  }

  login(body: LoginRequest): Promise<LoginResponse> {
    return firstValueFrom(this.http.post<LoginResponse>('/api/v1/auth/login', body));
  }

  loginTwoFactor(body: TwoFactorLoginRequest): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/auth/login/two-factor', body));
  }

  logout(): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/auth/logout', null));
  }

  register(body: RegisterRequest): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/auth/register', body));
  }

  forgotPassword(body: ForgotPasswordRequest): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/auth/password/forgot', body));
  }

  resetPassword(body: ResetPasswordRequest): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/auth/password/reset', body));
  }
}
