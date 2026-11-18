import { HttpErrorResponse } from '@angular/common/http';
import {
  decideGuestAccess,
  decideSetupAccess,
  decideSignedInAccess,
  safeReturnUrl,
  signInStateOf,
} from './access';

describe('signInStateOf', () => {
  it('reads 401 and 403 as signed out, other failures as an outage', () => {
    expect(signInStateOf(false, new HttpErrorResponse({ status: 401 }))).toBe('signedOut');
    expect(signInStateOf(false, new HttpErrorResponse({ status: 403 }))).toBe('signedOut');
    expect(signInStateOf(false, new HttpErrorResponse({ status: 502 }))).toBe('unavailable');
    expect(signInStateOf(false, new HttpErrorResponse({ status: 0 }))).toBe('unavailable');
    expect(signInStateOf(true, undefined)).toBe('signedIn');
  });
});

describe('access decisions', () => {
  it('sends everyone to setup while the instance has no user', () => {
    expect(decideSignedInAccess(true, 'signedOut')).toBe('setup');
    expect(decideGuestAccess(true, 'signedOut')).toBe('setup');
    expect(decideSetupAccess(true)).toBe('allow');
  });

  it('closes the setup page once the first user exists', () => {
    expect(decideSetupAccess(false)).toBe('home');
  });

  it('keeps the setup page reachable when the status is unknown', () => {
    expect(decideSetupAccess(null)).toBe('allow');
  });

  it('sends signed-out people to sign-in, and signed-in people away from it', () => {
    expect(decideSignedInAccess(false, 'signedOut')).toBe('signIn');
    expect(decideGuestAccess(false, 'signedIn')).toBe('home');
    expect(decideGuestAccess(false, 'signedOut')).toBe('allow');
  });

  it('shows the outage page instead of sign-in when the API does not answer', () => {
    expect(decideSignedInAccess(null, 'unavailable')).toBe('unavailable');
  });
});

describe('safeReturnUrl', () => {
  it('accepts paths inside the app', () => {
    expect(safeReturnUrl('/o/example/actions?filter=critical')).toBe(
      '/o/example/actions?filter=critical',
    );
  });

  it('rejects anything that leaves the app', () => {
    expect(safeReturnUrl('https://evil.example')).toBeNull();
    expect(safeReturnUrl('//evil.example/o')).toBeNull();
    expect(safeReturnUrl('/\\evil.example')).toBeNull();
    expect(safeReturnUrl('javascript:alert(1)')).toBeNull();
    expect(safeReturnUrl(null)).toBeNull();
  });
});
