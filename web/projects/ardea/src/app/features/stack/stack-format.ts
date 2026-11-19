import type { ComponentInput, ComponentView, StackSuggestionView } from '../../core/api/client';
import { CATALOG_GROUPS } from '../projects/component-format';

/** How precisely an entry names its version (master plan 17.1, ADR 046). */
export type Precision = 'none' | 'line' | 'exact';

/** When an entry raises a notice (ADR 044): the default, or every entry that names the product. */
export type Threshold = 'only_important' | 'everything';

export const THRESHOLDS: readonly { value: Threshold; label: string }[] = [
  { value: 'only_important', label: 'stack.threshold.onlyImportant' },
  { value: 'everything', label: 'stack.threshold.everything' },
];

/** What the precision of an entry means, said under its version. */
export const PRECISION_HINTS: Record<Precision, string> = {
  none: 'stack.precision.noneHint',
  line: 'stack.precision.lineHint',
  exact: 'stack.precision.exactHint',
};

/** A typical organization names 8 to 15 products (master plan 17.8). */
export const TYPICAL_STACK = { min: 8, max: 15 } as const;

/**
 * The precision a typed version gets unless someone says otherwise, as the API derives it for a
 * version typed by hand (ADR 046): one number («17») or a trailing wildcard («8.x») is a line,
 * anything else exact; nothing is any version.
 */
export function precisionOf(version: string | null | undefined): Precision {
  const v = (version ?? '').trim();
  if (!v) return 'none';
  return /^(\d+|\d+(\.\d+)*\.[xX*])$/.test(v) ? 'line' : 'exact';
}

/** The stack's entries, alphabetical: the order people look things up in. */
export function sortEntries(entries: readonly ComponentView[]): ComponentView[] {
  return [...entries].sort((a, b) =>
    a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
  );
}

/** A new stack entry: a catalogue product, any version, only important entries (ADR 044). */
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

/** The group of the catalogue search a product kind belongs to (master plan 11.6). */
export function groupOf(kind: string): string {
  return CATALOG_GROUPS.find((g) => g.kinds.includes(kind))?.id ?? 'other';
}

export interface SuggestionGroup {
  id: string;
  label: string;
  items: StackSuggestionView[];
}

/** The usual entries (master plan 17.8) by the groups of the catalogue search, in its order. */
export function groupCommon(suggestions: readonly StackSuggestionView[]): SuggestionGroup[] {
  const common = suggestions.filter((s) => s.reason === 'common');
  return CATALOG_GROUPS.map((g) => ({
    id: g.id,
    label: g.label,
    items: common.filter((s) => groupOf(s.kind) === g.id),
  })).filter((g) => g.items.length > 0);
}

/** Why a product is suggested, as a sentence: its companion rule (ADR 044) or the projects. */
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

/**
 * What the catalogue says about a name someone looks for (ADR 044, consequences): Microsoft Azure
 * is not one product, Microsoft names each service on its own; IIS and Exchange are named in
 * Windows Server records; a distribution covers itself, not the Linux kernel it ships. A companion
 * already in the stack needs no hint. The suggestions say the same once the entry is added.
 */
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
