import type { NoticeComponentRef, NoticeView } from '../../core/api/client';
import { num } from '../actions/action-format';

export interface Phrase {
  key: string;
  params: Record<string, string | number>;
}

type NoticeAnswer = 'affects_us' | 'not_affected';

function cvssOf(notice: Pick<NoticeView, 'cvssScore'>): number | null {
  const value = notice.cvssScore;
  if (value === null || value === '') return null;
  const score = typeof value === 'number' ? value : Number.parseFloat(value);
  return Number.isFinite(score) ? score : null;
}

export function cvssText(notice: Pick<NoticeView, 'cvssScore'>): string | null {
  const score = cvssOf(notice);
  return score === null ? null : score.toFixed(1);
}

export function isExploited(notice: Pick<NoticeView, 'exploitation'>): boolean {
  return notice.exploitation === 'active';
}

export function whatParams(notice: Pick<NoticeView, 'cvssScore' | 'exploitation'>): {
  severity: string;
  exploitation: string;
} {
  const score = cvssOf(notice);
  const severity =
    score === null
      ? 'unrated'
      : score >= 9
        ? 'critical'
        : score >= 7
          ? 'high'
          : score >= 4
            ? 'medium'
            : score > 0
              ? 'low'
              : 'unrated';
  const exploitation =
    notice.exploitation === 'active' || notice.exploitation === 'poc'
      ? notice.exploitation
      : 'none';
  return { severity, exploitation };
}

export function latestById(...lists: readonly (readonly NoticeView[])[]): NoticeView[] {
  const latest = new Map<string, NoticeView>();
  for (const list of lists) {
    for (const n of list) {
      const known = latest.get(n.id);
      if (!known || Date.parse(n.changedAt) >= Date.parse(known.changedAt)) latest.set(n.id, n);
    }
  }
  return [...latest.values()];
}

export function noticeOrder(
  a: Pick<NoticeView, 'exploitation' | 'cvssScore' | 'raisedAt' | 'id'>,
  b: Pick<NoticeView, 'exploitation' | 'cvssScore' | 'raisedAt' | 'id'>,
): number {
  const exploited = Number(isExploited(b)) - Number(isExploited(a));
  if (exploited) return exploited;
  const score = (cvssOf(b) ?? -1) - (cvssOf(a) ?? -1);
  if (score) return score;
  const raised = Date.parse(b.raisedAt) - Date.parse(a.raisedAt);
  if (raised) return raised;
  return b.id < a.id ? -1 : b.id > a.id ? 1 : 0;
}

export function isEarlier(notice: Pick<NoticeView, 'state' | 'isBackfill'>): boolean {
  return notice.state === 'open' && notice.isBackfill;
}

export function freshNotices<T extends Pick<NoticeView, 'state' | 'isBackfill'>>(
  notices: readonly T[],
): T[] {
  return notices.filter((n) => n.state === 'open' && !n.isBackfill);
}

export function reasonComponent(notice: Pick<NoticeView, 'components'>): NoticeComponentRef | null {
  const present = notice.components.filter((c) => !c.removed);
  return present.find((c) => c.inStack) ?? present[0] ?? notice.components[0] ?? null;
}

function productName(component: Pick<NoticeComponentRef, 'name' | 'version'>): string {
  return component.version ? `${component.name} ${component.version}` : component.name;
}

export function reasonPhrase(notice: Pick<NoticeView, 'components'>): Phrase {
  const c = reasonComponent(notice);
  if (!c) return { key: 'notices.reason.gone', params: {} };
  const product = productName(c);
  if (c.inStack || !c.project) return { key: 'notices.reason.stack', params: { product } };
  return { key: 'notices.reason.project', params: { product, project: c.project.name } };
}

export function affectNote(notice: Pick<NoticeView, 'components'>): Phrase {
  const c = reasonComponent(notice);
  if (c?.versionPrecision === 'line' && c.version)
    return { key: 'notices.affect.line', params: { line: c.version } };
  return { key: 'notices.affect.none', params: {} };
}

export function isNoisyReason(notice: Pick<NoticeView, 'components'>): boolean {
  const c = reasonComponent(notice);
  return !!c && c.noisy && c.noticeThreshold === 'only_important';
}

export function defaultPlaceId(notice: Pick<NoticeView, 'components'>): string | null {
  return reasonComponent(notice)?.project?.id ?? null;
}

export function openReminderFor<
  T extends { status: string; project: { id: string }; component: { name: string } },
>(
  notice: Pick<NoticeView, 'components'>,
  placeId: string | null,
  reminders: readonly T[],
): T | null {
  const c = reasonComponent(notice);
  if (!c || !placeId) return null;
  const name = c.name.toLowerCase();
  return (
    reminders.find(
      (r) =>
        r.status !== 'closed' &&
        r.project.id === placeId &&
        r.component.name.toLowerCase() === name,
    ) ?? null
  );
}

export interface AnsweredGroup {
  key: string;
  product: string;
  answer: NoticeAnswer;
  reminderNumber: number | null;
  notices: NoticeView[];
  decidedBy: string | null;
  decidedAt: string | null;
}

export function answeredGroups(notices: readonly NoticeView[]): AnsweredGroup[] {
  const groups = new Map<string, AnsweredGroup>();
  for (const n of notices) {
    if (n.state !== 'affects_us' && n.state !== 'not_affected') continue;
    const c = reasonComponent(n);
    const reminder = n.reminderNumber === null ? null : num(n.reminderNumber);
    const key = `${c?.id ?? '-'}|${n.state}|${reminder ?? ''}`;
    let group = groups.get(key);
    if (!group) {
      group = {
        key,
        product: c ? productName(c) : '?',
        answer: n.state,
        reminderNumber: reminder,
        notices: [],
        decidedBy: null,
        decidedAt: null,
      };
      groups.set(key, group);
    }
    group.notices.push(n);
    if (
      n.decidedAt &&
      (!group.decidedAt || Date.parse(n.decidedAt) > Date.parse(group.decidedAt))
    ) {
      group.decidedAt = n.decidedAt;
      group.decidedBy = n.decidedBy;
    }
  }
  const time = (g: AnsweredGroup) => (g.decidedAt ? Date.parse(g.decidedAt) : 0);
  return [...groups.values()]
    .map((g) => ({ ...g, notices: [...g.notices].sort(noticeOrder) }))
    .sort((a, b) => time(b) - time(a) || a.product.localeCompare(b.product));
}

export type CardMove = 'next' | 'previous' | 'affects' | 'notAffected';

export function cardMove(
  event: Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'metaKey' | 'altKey'>,
): CardMove | null {
  if (event.ctrlKey || event.metaKey || event.altKey) return null;
  switch (event.key) {
    case 'ArrowDown':
    case 'j':
    case 'J':
      return 'next';
    case 'ArrowUp':
    case 'k':
    case 'K':
      return 'previous';
    case '1':
      return 'affects';
    case '2':
      return 'notAffected';
    default:
      return null;
  }
}

export function countText(count: number, more: boolean): string {
  if (count > 99) return '99+';
  return more ? `${count}+` : String(count);
}
