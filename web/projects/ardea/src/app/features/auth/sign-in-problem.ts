import { problemMessageKey } from '../../core/api/problem';

export function signInProblemKey(error: unknown): string {
  return problemMessageKey(error, {
    codes: {
      invalid_credentials: 'auth.signIn.errors.invalid',
      locked_out: 'auth.signIn.errors.lockedOut',
    },
    statuses: {
      400: 'auth.signIn.errors.invalid',
      401: 'auth.signIn.errors.invalid',
      403: 'auth.signIn.errors.refused',
    },
    fallback: 'auth.signIn.errors.refused',
  });
}
