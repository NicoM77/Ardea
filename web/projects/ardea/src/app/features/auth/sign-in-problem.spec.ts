import { HttpErrorResponse } from '@angular/common/http';
import { signInProblemKey } from './sign-in-problem';

const fail = (status: number, body: unknown = null) =>
  new HttpErrorResponse({ status, error: body });

describe('signInProblemKey', () => {
  it('reads wrong credentials only from 400, 401 or the invalid_credentials code', () => {
    expect(signInProblemKey(fail(401))).toBe('auth.signIn.errors.invalid');
    expect(signInProblemKey(fail(400, { status: 400 }))).toBe('auth.signIn.errors.invalid');
    expect(signInProblemKey(fail(422, { code: 'invalid_credentials' }))).toBe(
      'auth.signIn.errors.invalid',
    );
  });

  it('keeps lock-outs apart from wrong credentials', () => {
    expect(signInProblemKey(fail(423, { code: 'locked_out' }))).toBe(
      'auth.signIn.errors.lockedOut',
    );
  });

  it('never calls a refusal or a server failure invalid credentials', () => {
    expect(signInProblemKey(fail(403))).toBe('auth.signIn.errors.refused');
    expect(signInProblemKey(fail(404))).toBe('auth.signIn.errors.refused');
    expect(signInProblemKey(fail(500))).toBe('errors.server');
    expect(signInProblemKey(fail(503))).toBe('errors.server');
    expect(signInProblemKey(fail(0))).toBe('errors.offline');
    expect(signInProblemKey(fail(429))).toBe('errors.tooManyRequests');
    expect(signInProblemKey(new Error('x'))).toBe('errors.server');
  });
});
