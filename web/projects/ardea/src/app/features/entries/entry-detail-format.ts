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
  const named = [affected.vendor, affected.product].filter(Boolean).join(' ');
  return named || affected.purl || affected.cpe || affected.productKey || '';
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
