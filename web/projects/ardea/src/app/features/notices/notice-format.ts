import type { NoticeComponentRef, NoticeView } from '../../core/api/client';
import { num } from '../actions/action-format';

/**
 * Words, order and groups of notices (master plan 17.3, 17.7; ADR 047; mockup notices.html). A notice
 * has no priority of its own (E23): it shows the source's CVSS score as a neutral pill and active
 * exploitation as the Exploited pill, and the list is ordered by them. Every rule here is pure, so the
 * page, the overview and the sidebar count read the same notices the same way.
 */

export interface Phrase {
  key: string;
  params: Record<string, string | number>;
}

/** The answers a person gives, as the API names them. */
export type NoticeAnswer = 'affects_us' | 'not_affected';

/** The CVSS score as a number, or `null` when the sources give none. */
export function cvssOf(notice: Pick<NoticeView, 'cvssScore'>): number | null {
  const value = notice.cvssScore;
  if (value === null || value === '') return null;
  const score = typeof value === 'number' ? value : Number.parseFloat(value);
  return Number.isFinite(score) ? score : null;
}

/** The score as the pill writes it, always with one decimal: «9.4», «10.0». */
export function cvssText(notice: Pick<NoticeView, 'cvssScore'>): string | null {
  const score = cvssOf(notice);
  return score === null ? null : score.toFixed(1);
}

export function isExploited(notice: Pick<NoticeView, 'exploitation'>): boolean {
  return notice.exploitation === 'active';
}

/**
 * «What is it?» from what the notice carries: the CVSS band in words (FIRST's qualitative scale) and
 * what is known of exploitation, for the sentence «A critical vulnerability, actively exploited.».
 */
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

/**
 * The newest known state of each notice, from the pages read and the answers given since (each a
 * `NoticeView` with its `changedAt`). A page read before an answer does not undo it on the screen; a
 * page read after it, or someone else's later change, wins.
 */
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

/**
 * The order of the inbox and of the overview's peek (E23, ADR 047 decision 9): actively exploited
 * first, then by the CVSS score (none last), then newest. The API pages in the same order; this keeps
 * it after a notice comes back through Undo.
 */
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

/**
 * Whether an open notice came quietly with a stack entry added or changed (ADR 047 decision 5): it
 * waits under «Earlier» and does not count as new.
 */
export function isEarlier(notice: Pick<NoticeView, 'state' | 'isBackfill'>): boolean {
  return notice.state === 'open' && notice.isBackfill;
}

/** Open notices that count as new: not raised quietly by a backfill. */
export function freshNotices<T extends Pick<NoticeView, 'state' | 'isBackfill'>>(
  notices: readonly T[],
): T[] {
  return notices.filter((n) => n.state === 'open' && !n.isBackfill);
}

/**
 * The component that gives the reason «Your stack names PostgreSQL 17»: a stack entry still in the
 * profile first, then any component still there, else the first one named.
 */
export function reasonComponent(notice: Pick<NoticeView, 'components'>): NoticeComponentRef | null {
  const present = notice.components.filter((c) => !c.removed);
  return present.find((c) => c.inStack) ?? present[0] ?? notice.components[0] ?? null;
}

/** The product as the stack names it: with its line when it has one («PostgreSQL 17»). */
export function productName(component: Pick<NoticeComponentRef, 'name' | 'version'>): string {
  return component.version ? `${component.name} ${component.version}` : component.name;
}

/** Why the notice asks (master plan 17.3): «Your stack names PostgreSQL 17» or a project's name. */
export function reasonPhrase(notice: Pick<NoticeView, 'components'>): Phrase {
  const c = reasonComponent(notice);
  if (!c) return { key: 'notices.reason.gone', params: {} };
  const product = productName(c);
  if (c.inStack || !c.project) return { key: 'notices.reason.stack', params: { product } };
  return { key: 'notices.reason.project', params: { product, project: c.project.name } };
}

/** «Does it affect us?» on a notice: always «Possibly», with what Ardea knows of the version. */
export function affectNote(notice: Pick<NoticeView, 'components'>): Phrase {
  const c = reasonComponent(notice);
  if (c?.versionPrecision === 'line' && c.version)
    return { key: 'notices.affect.line', params: { line: c.version } };
  return { key: 'notices.affect.none', params: {} };
}

/** Noisy products raise a notice only for exploited or warned-about entries (ADR 044). */
export function isNoisyReason(notice: Pick<NoticeView, 'components'>): boolean {
  const c = reasonComponent(notice);
  return !!c && c.noisy && c.noticeThreshold === 'only_important';
}

/** The place a reminder from «Affects us» goes when no project is chosen: the reason's project. */
export function defaultPlaceId(notice: Pick<NoticeView, 'components'>): string | null {
  return reasonComponent(notice)?.project?.id ?? null;
}

/**
 * The open reminder an «Affects us» answer would join (ADR 047 decision 7: a second notice for the same
 * product and place adds to the same reminder): an open action on the product's component in that place.
 */
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

/** One line of the answered view: the notices of one stack entry with the same answer and reminder. */
export interface AnsweredGroup {
  key: string;
  product: string;
  answer: NoticeAnswer;
  reminderNumber: number | null;
  notices: NoticeView[];
  /** The latest answer in the group: who and when. */
  decidedBy: string | null;
  decidedAt: string | null;
}

/**
 * The answered view (mockup notices.html): grouped by stack entry and answer, the group with the latest
 * answer first. A group of «affects us» is split by its reminder.
 */
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

/** What a key does on a notice card (mockup notices.html, with language-neutral answers). */
export type CardMove = 'next' | 'previous' | 'affects' | 'notAffected';

/**
 * The keyboard of the inbox: ↓ or J and ↑ or K move between the cards; 1 answers «Affects us» and 2
 * «Doesn't affect us», in the order of the buttons. Digits instead of the mockup's A and D, because
 * letters belong to one language («Betrifft uns» and «Betrifft uns nicht» start alike). Modified keys
 * stay with the browser.
 */
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

/** The count as the sidebar writes it: «99+» above 99, and «n+» while more pages were not read. */
export function countText(count: number, more: boolean): string {
  if (count > 99) return '99+';
  return more ? `${count}+` : String(count);
}
