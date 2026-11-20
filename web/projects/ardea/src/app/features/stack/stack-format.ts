import type { ComponentInput, ComponentView, StackSuggestionView } from '../../core/api/client';
import { CATALOG_GROUPS } from '../projects/component-format';

export type Precision = 'none' | 'line' | 'exact';

export type Threshold = 'only_important' | 'everything';

export const THRESHOLDS: readonly { value: Threshold; label: string }[] = [
  { value: 'only_important', label: 'stack.threshold.onlyImportant' },
  { value: 'everything', label: 'stack.threshold.everything' },
];

export const PRECISION_HINTS: Record<Precision, string> = {
  none: 'stack.precision.noneHint',
  line: 'stack.precision.lineHint',
  exact: 'stack.precision.exactHint',
};

export const TYPICAL_STACK = { min: 8, max: 15 } as const;

export function precisionOf(version: string | null | undefined): Precision {
  const v = (version ?? '').trim();
  if (!v) return 'none';
  return /^(\d+|\d+(\.\d+)*\.[xX*])$/.test(v) ? 'line' : 'exact';
}

export function sortEntries(entries: readonly ComponentView[]): ComponentView[] {
  return [...entries].sort((a, b) =>
    a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
  );
}

export function entryInput(change: Partial<ComponentInput>): ComponentInput {
  return {
    id: null,
    ref: null,
    productKey: null,
    customVendor: null,
    customName: null,
    kind: null,
    version: null,
    packaging: null,
    parentId: null,
    parentRef: null,
    exposure: null,
    criticality: null,
    protection: null,
    protectionNote: null,
    ...change,
  };
}

export function groupOf(kind: string): string {
  return CATALOG_GROUPS.find((g) => g.kinds.includes(kind))?.id ?? 'other';
}

export interface SuggestionGroup {
  id: string;
  label: string;
  items: StackSuggestionView[];
}

export function groupCommon(suggestions: readonly StackSuggestionView[]): SuggestionGroup[] {
  const common = suggestions.filter((s) => s.reason === 'common');
  return CATALOG_GROUPS.map((g) => ({
    id: g.id,
    label: g.label,
    items: common.filter((s) => groupOf(s.kind) === g.id),
  })).filter((g) => g.items.length > 0);
}

export function suggestionReason(s: StackSuggestionView): {
  key: string;
  params: Record<string, string | number>;
} {
  if (s.reason === 'companion') {
    return s.productKey === 'linux-kernel'
      ? { key: 'stack.suggest.kernel', params: { basis: s.basis ?? '' } }
      : s.productKey === 'windows-server'
        ? { key: 'stack.suggest.windows', params: { basis: s.basis ?? '' } }
        : { key: 'stack.suggest.companion', params: { basis: s.basis ?? '' } };
  }
  return {
    key: 'stack.suggest.projects',
    params: { projects: s.projects.join(', '), count: s.projects.length },
  };
}

export function searchHint(query: string, taken: ReadonlySet<string> = new Set()): string | null {
  const q = query.trim();
  if (/\bazure/i.test(q)) return 'stack.add.azureHint';
  if (!taken.has('windows-server')) {
    if (/\biis\b|internet information/i.test(q)) return 'stack.add.iisHint';
    if (/\bexchange/i.test(q)) return 'stack.add.exchangeHint';
  }
  if (
    !taken.has('linux-kernel') &&
    /\b(ubuntu|debian|red hat|rhel|suse|sles|rocky|alma|alpine|oracle linux)/i.test(q)
  )
    return 'stack.add.distributionHint';
  return null;
}
