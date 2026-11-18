import { TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import { IcuTranspiler } from './icu-transpiler';

describe('IcuTranspiler', () => {
  function transpiler() {
    TestBed.configureTestingModule({
      providers: [
        provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
        IcuTranspiler,
      ],
    });
    return TestBed.inject(IcuTranspiler);
  }
  const run = (t: IcuTranspiler, value: string, params?: Record<string, unknown>) =>
    t.transpile({ value, params, translation: {}, key: 'test' });

  it('chooses the plural form by count', () => {
    const t = transpiler();
    const text = '{count, plural, =0 {none} one {# job} other {# jobs}}';
    expect(run(t, text, { count: 0 })).toBe('none');
    expect(run(t, text, { count: 1 })).toBe('1 job');
    expect(run(t, text, { count: 12 })).toBe('12 jobs');
  });

  it('fills arguments and leaves plain text untouched', () => {
    const t = transpiler();
    expect(run(t, 'A test e-mail is on its way to {to}.', { to: 'a@example.org' })).toBe(
      'A test e-mail is on its way to a@example.org.',
    );
    expect(run(t, 'Sign in')).toBe('Sign in');
  });

  it('shows a message with a missing argument as written instead of failing', () => {
    expect(run(transpiler(), 'Step {current} of {total}', { current: 1 })).toBe(
      'Step {current} of {total}',
    );
  });

  it('formats numbers in the active language', () => {
    const t = transpiler();
    t.onLangChanged('de');
    expect(run(t, '{count, plural, other {# Einträge}}', { count: 6432 })).toBe('6.432 Einträge');
  });
});
