import type { AffectedView, EntrySummary } from '../../core/api/client';
import {
  affectedStatusLabel,
  productName,
  sourceLabel,
  versionRange,
  webUrl,
} from './entry-detail-format';
import { filtersFrom, listParams, mergePage, signalPills } from './entry-format';

const entry = (key: string, extra: Partial<EntrySummary> = {}): EntrySummary => ({
  key,
  kind: 'vulnerability',
  title: null,
  state: 'published',
  cvssScore: null,
  severity: 'unrated',
  exploitation: 'none',
  epssClass: null,
  fixAvailable: false,
  authorityWarning: false,
  publishedAt: null,
  changedAt: '2026-11-18T08:00:00Z',
  aliases: [],
  ...extra,
});

describe('entry list filters', () => {
  it('accept only values the list knows from the address', () => {
    expect(filtersFrom({ q: '  CVE-2026-1 ', kind: 'warning', since: 'week' })).toEqual({
      q: 'CVE-2026-1',
      kind: 'warning',
      since: 'week',
    });
    expect(filtersFrom({ kind: 'everything', since: 'year' })).toEqual({
      q: '',
      kind: '',
      since: '',
    });
  });

  it('send only the filters that are set, the time as a timestamp', () => {
    const now = new Date('2026-11-18T12:00:00Z');
    expect(listParams({ q: '', kind: '', since: '' }, undefined, now)).toEqual({ limit: 50 });
    expect(listParams({ q: 'openssl', kind: 'vulnerability', since: 'day' }, 'c1', now)).toEqual({
      q: 'openssl',
      kind: 'vulnerability',
      changedSince: '2026-11-17T12:00:00.000Z',
      cursor: 'c1',
      limit: 50,
    });
  });
});

describe('mergePage', () => {
  it('replaces the rows with a first page', () => {
    expect(mergePage([entry('A')], [entry('B')], true).map((e) => e.key)).toEqual(['B']);
  });

  it('appends a further page without repeating an entry that moved between pages', () => {
    const merged = mergePage([entry('A'), entry('B')], [entry('B'), entry('C')], false);
    expect(merged.map((e) => e.key)).toEqual(['A', 'B', 'C']);
  });
});

describe('signalPills', () => {
  it('puts active exploitation first, and shows nothing for a quiet entry', () => {
    expect(signalPills(entry('A'))).toEqual([]);
    expect(
      signalPills(entry('A', { exploitation: 'active', epssClass: 'high', fixAvailable: true })),
    ).toEqual([
      { key: 'entries.signals.exploited', tone: 'alert' },
      { key: 'entries.signals.epssHigh', tone: 'neutral' },
      { key: 'entries.signals.fix', tone: 'done' },
    ]);
  });
});

describe('versionRange', () => {
  const range = (extra: Partial<AffectedView>) =>
    versionRange({
      vendor: null,
      product: null,
      productKey: null,
      cpe: null,
      purl: null,
      versionScheme: null,
      introduced: null,
      fixed: null,
      lastAffected: null,
      exact: null,
      status: 'affected',
      defaultStatus: 'unaffected',
      source: 'cve-program',
      ...extra,
    });

  it('reads the four ways a source describes versions', () => {
    expect(range({ exact: '2.4.1' }).key).toBe('entryDetail.range.exact');
    expect(range({ introduced: '2.0', fixed: '2.4.2' })).toEqual({
      key: 'entryDetail.range.fromBefore',
      params: { from: '2.0', to: '2.4.2' },
    });
    expect(range({ introduced: '0', fixed: '1.1' }).key).toBe('entryDetail.range.before');
    expect(range({ lastAffected: '3.3' }).key).toBe('entryDetail.range.upTo');
    expect(range({}).key).toBe('entryDetail.range.any');
  });
});

describe('webUrl', () => {
  it('links only web addresses from source data', () => {
    expect(webUrl('https://www.cve.org/CVERecord?id=CVE-2026-1')).toBe(
      'https://www.cve.org/CVERecord?id=CVE-2026-1',
    );
    expect(webUrl('javascript:alert(1)')).toBeNull();
    expect(webUrl('not a url')).toBeNull();
    expect(webUrl(null)).toBeNull();
  });
});

describe('the affected products and the sources in words', () => {
  const named = (vendor: string | null, product: string | null) =>
    productName({
      vendor,
      product,
      productKey: null,
      cpe: null,
      purl: null,
      versionScheme: null,
      introduced: null,
      fixed: null,
      lastAffected: null,
      exact: null,
      status: 'affected',
      defaultStatus: 'unknown',
      source: 'cve-program',
    });

  it('names a product once when the vendor repeats it', () => {
    expect(named('Linux', 'Linux')).toBe('Linux');
    expect(named('Microsoft', 'Microsoft Exchange Server')).toBe('Microsoft Exchange Server');
    expect(named('Oracle', 'MySQL')).toBe('Oracle MySQL');
    expect(named(null, 'nginx')).toBe('nginx');
  });

  it('writes the status of a range as a word', () => {
    expect(affectedStatusLabel('unaffected')).toBe('entryDetail.affectedStatus.unaffected');
    expect(affectedStatusLabel('something new')).toBe('entryDetail.affectedStatus.unknown');
  });

  it('names a source by its name, with the assigner after it, and says when Ardea derived a value', () => {
    const names = new Map([['cve-program', 'CVE Program']]);
    expect(sourceLabel('cve-program', names)).toEqual({ key: null, name: 'CVE Program' });
    expect(sourceLabel('cve-program/Linux', names)).toEqual({
      key: null,
      name: 'CVE Program · Linux',
    });
    expect(sourceLabel('cisa-kev', names)).toEqual({ key: null, name: 'cisa-kev' });
    expect(sourceLabel('derived', names).key).toBe('entryDetail.derived');
  });
});
