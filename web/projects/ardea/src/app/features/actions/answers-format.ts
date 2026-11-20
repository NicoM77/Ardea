import type { AffectedRange } from '../../core/api/client';

export interface Phrase {
  key: string;
  params: Record<string, string>;
}

function lower(value: string | null): string | null {
  return value && value !== '0' && value !== '*' ? value : null;
}

export function rangePhrase(range: AffectedRange): Phrase {
  const from = lower(range.introduced);
  if (range.exact) return { key: 'answers.range.exact', params: { version: range.exact } };
  if (from && range.fixed)
    return { key: 'answers.range.fromBefore', params: { from, to: range.fixed } };
  if (from && range.lastAffected)
    return { key: 'answers.range.fromTo', params: { from, to: range.lastAffected } };
  if (range.fixed) return { key: 'answers.range.before', params: { to: range.fixed } };
  if (range.lastAffected) return { key: 'answers.range.upTo', params: { to: range.lastAffected } };
  if (from) return { key: 'answers.range.from', params: { from } };
  return { key: 'answers.range.any', params: {} };
}

export const AFFECTED_LABELS: Readonly<Record<string, string>> = {
  affected: 'answers.affected.yes',
  unknown: 'answers.affected.unknown',
  not_affected: 'answers.affected.no',
};

export function affectedLabel(verdict: string): string {
  return AFFECTED_LABELS[verdict] ?? 'answers.affected.unknown';
}

export const CHECK_LABELS: Readonly<Record<string, string>> = {
  in_range: 'answers.check.inRange',
  out_of_range: 'answers.check.outside',
  unknown: 'answers.check.unknown',
};

export function checkLabel(versionVerdict: string | null): string {
  return versionVerdict === null
    ? 'answers.check.weak'
    : (CHECK_LABELS[versionVerdict] ?? 'answers.check.unknown');
}

export const REASON_LABELS: Readonly<Record<string, string>> = {
  no_installed_version: 'answers.reason.noInstalledVersion',
  distribution_package: 'answers.reason.distributionPackage',
  installed_version_not_readable: 'answers.reason.installedNotReadable',
  range_not_readable: 'answers.reason.rangeNotReadable',
  no_comparable_range: 'answers.reason.noComparableRange',
  no_scheme: 'answers.reason.noScheme',
  in_unknown_range: 'answers.reason.inUnknownRange',
};

export function reasonLabel(reason: string | null): string | null {
  return reason ? (REASON_LABELS[reason] ?? null) : null;
}

export const TODO_LABELS: Readonly<Record<string, string>> = {
  update: 'answers.todo.update',
  source_guidance: 'answers.todo.guidance',
  no_fix: 'answers.todo.noFix',
};

export function quoteLang(interfaceLang: string): string | null {
  return interfaceLang.toLowerCase().startsWith('en') ? null : 'en';
}
