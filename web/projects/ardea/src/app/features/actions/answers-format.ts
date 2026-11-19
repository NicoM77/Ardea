import type { AffectedRange } from '../../core/api/client';

/**
 * The three answers (master plan 7.5, 11.2; task 3.6) arrive as data; the interface words them in
 * the reader's language. Only the source's own text is quoted as written.
 */

export interface Phrase {
  key: string;
  params: Record<string, string>;
}

/** A lower bound of «0» means «every version up to»; the record says nothing there. */
function lower(value: string | null): string | null {
  return value && value !== '0' && value !== '*' ? value : null;
}

/** An affected range as words: «17 to before 17.12», «up to 2.4», «exactly 1.0.3». */
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

/** «Are we affected?» in one word. */
export const AFFECTED_LABELS: Readonly<Record<string, string>> = {
  affected: 'answers.affected.yes',
  unknown: 'answers.affected.unknown',
  not_affected: 'answers.affected.no',
};

export function affectedLabel(verdict: string): string {
  return AFFECTED_LABELS[verdict] ?? 'answers.affected.unknown';
}

/** The version check of one component, as a pill. */
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

/** Why a version could not be checked (the version check's rule, in snake case). */
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

/** «What to do», by its kind. */
export const TODO_LABELS: Readonly<Record<string, string>> = {
  update: 'answers.todo.update',
  source_guidance: 'answers.todo.guidance',
  no_fix: 'answers.todo.noFix',
};

/**
 * The language of a quoted source text. The texts the answers quote (CVE, OSV, GitHub, vendor
 * advisories) are English; when the reader's interface is in another language, the quotation is
 * marked as English so screen readers pronounce it right (master plan 11.8).
 */
export function quoteLang(interfaceLang: string): string | null {
  return interfaceLang.toLowerCase().startsWith('en') ? null : 'en';
}
