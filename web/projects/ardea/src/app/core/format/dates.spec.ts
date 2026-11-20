import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { Dates, formatDate, formatDateTime, formatRelative, formatTime } from './dates';

const AT = '2026-11-20T14:05:00Z';

describe('formatDate', () => {
  it('writes the medium date in the locale', () => {
    expect(formatDate('en', AT)).toBe('Nov 20, 2026');
    expect(formatDate('de-CH', AT)).toBe('20.11.2026');
  });

  it('takes the longer styles', () => {
    expect(formatDate('en', AT, 'long')).toBe('November 20, 2026');
    expect(formatDate('en', AT, 'full')).toBe('Friday, November 20, 2026');
  });

  it('keeps a calendar day on its day in every time zone', () => {
    expect(formatDate('en', '2026-11-16')).toBe('Nov 16, 2026');
  });

  it('accepts a timestamp and a Date', () => {
    expect(formatDate('en', Date.parse(AT))).toBe('Nov 20, 2026');
    expect(formatDate('en', new Date(AT))).toBe('Nov 20, 2026');
  });

  it('returns what it cannot read instead of failing', () => {
    expect(formatDate('en', 'not a date')).toBe('not a date');
  });
});

describe('formatDateTime and formatTime', () => {
  it('adds the short time to the date', () => {
    const text = formatDateTime('en', AT);
    expect(text).toContain('Nov 20, 2026');
    expect(text).toBe(
      `Nov 20, 2026, ${new Intl.DateTimeFormat('en', { timeStyle: 'short' }).format(new Date(AT))}`,
    );
  });

  it('writes the time alone', () => {
    expect(formatTime('en', AT)).toBe(
      new Intl.DateTimeFormat('en', { timeStyle: 'short' }).format(new Date(AT)),
    );
  });

  it('returns what it cannot read instead of failing', () => {
    expect(formatDateTime('en', '')).toBe('');
    expect(formatTime('en', 'soon')).toBe('soon');
  });
});

describe('formatRelative', () => {
  const now = Date.parse(AT);
  const ago = (ms: number) => new Date(now - ms).toISOString();

  it('says now within a minute, either way', () => {
    expect(formatRelative('en', ago(20_000), now)).toBe('now');
    expect(formatRelative('en', ago(-20_000), now)).toBe('now');
  });

  it('picks the largest unit that fits', () => {
    expect(formatRelative('en', ago(5 * 60_000), now)).toBe('5 minutes ago');
    expect(formatRelative('en', ago(3 * 3_600_000), now)).toBe('3 hours ago');
    expect(formatRelative('en', ago(26 * 3_600_000), now)).toBe('yesterday');
    expect(formatRelative('en', ago(3 * 86_400_000), now)).toBe('3 days ago');
    expect(formatRelative('en', ago(15 * 86_400_000), now)).toBe('2 weeks ago');
    expect(formatRelative('en', ago(95 * 86_400_000), now)).toBe('3 months ago');
    expect(formatRelative('en', ago(800 * 86_400_000), now)).toBe('2 years ago');
  });

  it('looks ahead too', () => {
    expect(formatRelative('en', ago(-2 * 3_600_000), now)).toBe('in 2 hours');
    expect(formatRelative('en', ago(-26 * 3_600_000), now)).toBe('tomorrow');
  });

  it('speaks the locale', () => {
    expect(formatRelative('de', ago(3 * 86_400_000), now)).toBe('vor 3 Tagen');
  });

  it('returns what it cannot read instead of failing', () => {
    expect(formatRelative('en', 'never', now)).toBe('never');
  });
});

describe('Dates', () => {
  let lang: string;

  beforeEach(() => {
    lang = 'en';
    TestBed.configureTestingModule({
      providers: [{ provide: TranslocoService, useValue: { getActiveLang: () => lang } }],
    });
  });

  it('formats in the active language of the app', () => {
    const dates = TestBed.inject(Dates);
    expect(dates.date(AT)).toBe('Nov 20, 2026');
    expect(dates.date(AT, 'long')).toBe('November 20, 2026');
    expect(dates.dateTime(AT)).toBe(formatDateTime('en', AT));
    expect(dates.time(AT)).toBe(formatTime('en', AT));
    expect(dates.relative(AT, Date.parse(AT) + 120_000)).toBe('2 minutes ago');

    lang = 'de-CH';
    expect(dates.date(AT)).toBe('20.11.2026');
  });
});
