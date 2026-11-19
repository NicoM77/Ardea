/** The lists of the catalogue that learns (master plan 6.9), with their translation keys written out in full. */

export type LearningView = 'review' | 'open' | 'in_effect' | 'decided';

export const LEARNING_VIEWS: readonly { value: LearningView; label: string }[] = [
  { value: 'review', label: 'catalogLearning.view.review' },
  { value: 'open', label: 'catalogLearning.view.open' },
  { value: 'in_effect', label: 'catalogLearning.view.inEffect' },
  { value: 'decided', label: 'catalogLearning.view.decided' },
];

export const OUTCOME_LABELS: Readonly<Record<string, string>> = {
  open: 'catalogLearning.outcome.open',
  pending: 'catalogLearning.outcome.pending',
  alias: 'catalogLearning.outcome.alias',
  new: 'catalogLearning.outcome.new',
  unclear: 'catalogLearning.outcome.unclear',
  rejected: 'catalogLearning.outcome.rejected',
  withdrawn: 'catalogLearning.outcome.withdrawn',
};

export const LEARNED_BY_LABELS: Readonly<Record<string, string>> = {
  rule: 'catalogLearning.by.rule',
  model: 'catalogLearning.by.model',
  admin: 'catalogLearning.by.admin',
};

export const HOLD_LABELS: Readonly<Record<string, string>> = {
  confidence: 'catalogLearning.hold.confidence',
  suspicious_input: 'catalogLearning.hold.suspicious',
  invalid_output: 'catalogLearning.hold.invalid',
  unclear: 'catalogLearning.hold.unclear',
  check: 'catalogLearning.hold.check',
};

/** The kinds and version schemes the catalogue knows (data/catalog.schema.json). */
export const KINDS = [
  'os',
  'runtime',
  'framework',
  'cms',
  'database',
  'messaging',
  'container',
  'network',
  'virtualization',
  'library',
  'plugin',
  'service',
  'other',
] as const;

export const SCHEMES = [
  'go',
  'maven',
  'numeric',
  'nuget',
  'packagist',
  'pep440',
  'rpm',
  'semver',
] as const;

/** A share from 0 to 1 as a percentage in the reader's language; a dash before the first collection. */
export function share(value: number | string | null | undefined, locale: string): string {
  if (value === null || value === undefined || value === '') return '–';
  const n = Number(value);
  if (!Number.isFinite(n)) return '–';
  return new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(n);
}
