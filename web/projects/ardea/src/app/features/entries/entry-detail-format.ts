import type { AffectedView } from '../../core/api/client';

export function versionRange(affected: AffectedView): {
  key: string;
  params: Record<string, string>;
} {
  const { introduced, fixed, lastAffected, exact } = affected;
  if (exact) return { key: 'entryDetail.range.exact', params: { version: exact } };
  const from = introduced && introduced !== '0' ? introduced : null;
  if (from && fixed) return { key: 'entryDetail.range.fromBefore', params: { from, to: fixed } };
  if (from && lastAffected)
    return { key: 'entryDetail.range.fromUpTo', params: { from, to: lastAffected } };
  if (fixed) return { key: 'entryDetail.range.before', params: { to: fixed } };
  if (lastAffected) return { key: 'entryDetail.range.upTo', params: { to: lastAffected } };
  if (from) return { key: 'entryDetail.range.from', params: { from } };
  return { key: 'entryDetail.range.any', params: {} };
}

export function productName(affected: AffectedView): string {
  const { vendor, product } = affected;
  const named =
    vendor && product && !product.toLowerCase().startsWith(vendor.toLowerCase())
      ? `${vendor} ${product}`
      : product || vendor;
  return named || affected.purl || affected.cpe || affected.productKey || '';
}

const AFFECTED_STATUS_LABELS: Readonly<Record<string, string>> = {
  affected: 'entryDetail.affectedStatus.affected',
  unaffected: 'entryDetail.affectedStatus.unaffected',
  unknown: 'entryDetail.affectedStatus.unknown',
};

export function affectedStatusLabel(status: string): string {
  return AFFECTED_STATUS_LABELS[status] ?? 'entryDetail.affectedStatus.unknown';
}

export const FIELD_LABELS: Readonly<Record<string, string>> = {
  title: 'entryDetail.fields.title',
  summary: 'entryDetail.fields.summary',
  description: 'entryDetail.fields.description',
  severity: 'entryDetail.fields.severity',
  exploitation: 'entryDetail.fields.exploitation',
  epss: 'entryDetail.fields.epss',
  authorityWarning: 'entryDetail.fields.authorityWarning',
};

export const CHANGE_LABELS: Readonly<Record<string, string>> = {
  created: 'entryDetail.changes.created',
  state: 'entryDetail.changes.state',
  severity: 'entryDetail.changes.severity',
  affected: 'entryDetail.changes.affected',
  exploitation: 'entryDetail.changes.exploitation',
  fix: 'entryDetail.changes.fix',
  authority: 'entryDetail.changes.authority',
  aliases: 'entryDetail.changes.aliases',
  text: 'entryDetail.changes.text',
  references: 'entryDetail.changes.references',
  weaknesses: 'entryDetail.changes.weaknesses',
  remediation: 'entryDetail.changes.remediation',
  material: 'entryDetail.changes.material',
  epss: 'entryDetail.changes.epss',
};

export interface SourceLabel {
  key: string | null;
  name: string;
}

export function sourceLabel(value: string, names: ReadonlyMap<string, string>): SourceLabel {
  if (value === 'derived') return { key: 'entryDetail.derived', name: '' };
  const slash = value.indexOf('/');
  const source = slash > 0 ? value.slice(0, slash) : value;
  const by = slash > 0 ? value.slice(slash + 1) : '';
  const name = names.get(source) ?? source;
  return { key: null, name: by ? `${name} · ${by}` : name };
}

export function webUrl(value: string | null | undefined): string | null {
  if (!value) return null;
  try {
    const url = new URL(value);
    return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : null;
  } catch {
    return null;
  }
}
