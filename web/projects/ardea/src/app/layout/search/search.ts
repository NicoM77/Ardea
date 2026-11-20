import type { LucideIconData } from '@lucide/angular';

export interface SearchItem {
  label: string;
  group: string;
  url: string;
  icon: LucideIconData;
  keywords?: string;
}

export function filterSearchItems(items: readonly SearchItem[], query: string): SearchItem[] {
  const words = fold(query).split(/\s+/).filter(Boolean);
  if (words.length === 0) return [...items];
  return items.filter((item) => {
    const haystack = fold(`${item.label} ${item.keywords ?? ''}`);
    return words.every((word) => haystack.includes(word));
  });
}

export function moveActive(index: number, delta: 1 | -1, length: number): number {
  if (length === 0) return -1;
  if (index < 0) return delta === 1 ? 0 : length - 1;
  return (index + delta + length) % length;
}

export function opensSearch(
  event: Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'metaKey' | 'altKey'>,
  typing: boolean,
): boolean {
  if (event.altKey) return false;
  if ((event.key === 'k' || event.key === 'K') && (event.ctrlKey || event.metaKey)) return true;
  return event.key === '/' && !typing && !event.ctrlKey && !event.metaKey;
}

function fold(value: string): string {
  return value
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .trim();
}
