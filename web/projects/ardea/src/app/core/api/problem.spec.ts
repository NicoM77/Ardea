import { HttpErrorResponse } from '@angular/common/http';
import { problemMessageKey } from './problem';

const failure = (status: number, body: unknown = null) =>
  new HttpErrorResponse({ status, error: body, url: '/api/v1/auth/login' });

const messages = {
  codes: { locked_out: 'auth.locked' },
  statuses: { 401: 'auth.invalid' },
  fallback: 'auth.rejected',
};

describe('problemMessageKey', () => {
  it('prefers the problem code over the status', () => {
    expect(problemMessageKey(failure(401, { status: 401, code: 'locked_out' }), messages)).toBe(
      'auth.locked',
    );
  });

  it('falls back to the status when the code is unknown to the form', () => {
    expect(problemMessageKey(failure(401, { status: 401, code: 'something_new' }), messages)).toBe(
      'auth.invalid',
    );
  });

  it('says the server is unreachable when no response came back', () => {
    expect(problemMessageKey(failure(0), messages)).toBe('errors.offline');
  });

  it('says to wait on rate limiting, and blames the server on 5xx', () => {
    expect(problemMessageKey(failure(429), messages)).toBe('errors.tooManyRequests');
    expect(problemMessageKey(failure(503, '<html>Bad gateway</html>'), messages)).toBe(
      'errors.server',
    );
  });

  it("uses the form's own sentence for any other client error", () => {
    expect(
      problemMessageKey(failure(422, { status: 422, title: 'Validation failed' }), messages),
    ).toBe('auth.rejected');
  });

  it('treats a non-HTTP failure as a server error, not as wrong input', () => {
    expect(problemMessageKey(new Error('boom'), messages)).toBe('errors.server');
  });
});
