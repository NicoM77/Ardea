import { HttpErrorResponse } from '@angular/common/http';

export type SignInState = 'signedIn' | 'signedOut' | 'unavailable';

export type AccessDecision = 'allow' | 'setup' | 'signIn' | 'home' | 'unavailable';

export function signInStateOf(hasValue: boolean, error: unknown): SignInState {
  if (hasValue) return 'signedIn';
  if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
    return 'signedOut';
  }
  return 'unavailable';
}

export function decideSignedInAccess(
  setupRequired: boolean | null,
  me: SignInState,
): AccessDecision {
  if (setupRequired === true) return 'setup';
  if (me === 'signedIn') return 'allow';
  if (me === 'signedOut') return 'signIn';
  return 'unavailable';
}

export function decideGuestAccess(setupRequired: boolean | null, me: SignInState): AccessDecision {
  if (setupRequired === true) return 'setup';
  if (me === 'signedIn') return 'home';
  return 'allow';
}

export function decideSetupAccess(setupRequired: boolean | null): AccessDecision {
  return setupRequired === false ? 'home' : 'allow';
}

export function safeReturnUrl(value: string | null | undefined): string | null {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return null;
  }
  return value;
}
