import { HttpErrorResponse } from '@angular/common/http';

/** What the app knows about the person in front of it. */
export type SignInState = 'signedIn' | 'signedOut' | 'unavailable';

/** Where a guard sends someone. */
export type AccessDecision = 'allow' | 'setup' | 'signIn' | 'home' | 'unavailable';

/** Reads the outcome of `GET /api/v1/me`: 401 and 403 mean signed out, anything else is an outage. */
export function signInStateOf(hasValue: boolean, error: unknown): SignInState {
  if (hasValue) return 'signedIn';
  if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
    return 'signedOut';
  }
  return 'unavailable';
}

/**
 * Pages behind sign-in. A fresh instance goes to setup before anything else; an unknown setup
 * state (the status request failed) does not block, because `me` tells us more.
 */
export function decideSignedInAccess(
  setupRequired: boolean | null,
  me: SignInState,
): AccessDecision {
  if (setupRequired === true) return 'setup';
  if (me === 'signedIn') return 'allow';
  if (me === 'signedOut') return 'signIn';
  return 'unavailable';
}

/** Sign-in, registration and password pages: open to signed-out people only. */
export function decideGuestAccess(setupRequired: boolean | null, me: SignInState): AccessDecision {
  if (setupRequired === true) return 'setup';
  if (me === 'signedIn') return 'home';
  return 'allow';
}

/** The setup page exists only while the instance has no user. */
export function decideSetupAccess(setupRequired: boolean | null): AccessDecision {
  return setupRequired === false ? 'home' : 'allow';
}

/**
 * The address to return to after signing in. Only paths inside this app: an absolute or
 * protocol-relative URL in the query string would turn the sign-in page into an open redirect.
 */
export function safeReturnUrl(value: string | null | undefined): string | null {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return null;
  }
  return value;
}
