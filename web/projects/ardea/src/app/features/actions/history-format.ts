import type { UiPriorityLevel } from 'ui';
import type { ActionEventView, FindingView, MemberView } from '../../core/api/client';
import { priorityOfLevel, SYSTEM_REASON_LABELS } from './action-format';

/** One line of an action's history (master plan 8.2): what happened, in the reader's words. */
export interface HistoryLine {
  id: string;
  kind: string;
  at: string;
  key: string;
  params: Record<string, string | number>;
  /** A person's own words (a note, a reason), quoted as written. */
  quote: string | null;
  /** A change of priority: from (null when it started) to. */
  change: [UiPriorityLevel | null, UiPriorityLevel] | null;
  /** A system reason, already a translation key. */
  reason: string | null;
}

type Data = Record<string, unknown>;

function text(data: Data, name: string): string | null {
  const value = data[name];
  return typeof value === 'string' && value.trim() ? value : null;
}

function data(event: ActionEventView): Data {
  return event.data && typeof event.data === 'object' ? (event.data as Data) : {};
}

/** Newest first, each with the words for its kind; unknown kinds keep their name. */
export function historyLines(
  events: readonly ActionEventView[],
  members: readonly MemberView[],
  findings: readonly FindingView[],
): HistoryLine[] {
  const person = (id: string | null | undefined) =>
    (id && members.find((m) => m.userId === id)?.displayName) || null;
  const entry = (findingId: string | null) =>
    findings.find((f) => f.id === findingId)?.entry.key ?? '';

  return [...events].reverse().map((event) => {
    const d = data(event);
    const actor = person(event.actor);
    const line: HistoryLine = {
      id: event.id,
      kind: event.kind,
      at: event.at,
      key: 'actionDetail.history.other',
      params: { kind: event.kind },
      quote: null,
      change: null,
      reason: null,
    };
    // A person who left the organization, or none: the sentence says «Someone» (ICU select).
    const who = actor ?? 'someone';
    switch (event.kind) {
      case 'action.raised':
        line.key = 'actionDetail.history.raised';
        line.params = { findings: Number(d['findings'] ?? 1) };
        line.change = [null, priorityOfLevel(d['level'] as number)];
        break;
      case 'action.level_raised':
      case 'action.level_lowered':
        line.key =
          event.kind === 'action.level_raised'
            ? 'actionDetail.history.levelRaised'
            : 'actionDetail.history.levelLowered';
        line.params = {};
        line.change = [priorityOfLevel(d['from'] as number), priorityOfLevel(d['to'] as number)];
        break;
      case 'action.exploitation_escalated':
        line.key =
          d['to'] === 'active'
            ? 'actionDetail.history.exploited'
            : 'actionDetail.history.proofOfConcept';
        line.params = {};
        break;
      case 'action.fix_available':
        line.key = 'actionDetail.history.fixAvailable';
        line.params = { version: text(d, 'targetVersion') ?? '' };
        break;
      case 'action.withdrawn':
        line.key = 'actionDetail.history.withdrawn';
        line.params = {};
        line.reason = SYSTEM_REASON_LABELS[text(d, 'reason') ?? ''] ?? null;
        break;
      case 'action.assigned': {
        const to = person(text(d, 'to'));
        line.key = to ? 'actionDetail.history.assigned' : 'actionDetail.history.unassigned';
        line.params = { who, to: to ?? '' };
        break;
      }
      case 'action.status_changed':
      case 'finding.status_changed': {
        const to = text(d, 'to') ?? '';
        const from = text(d, 'from') ?? '';
        const finding = event.kind === 'finding.status_changed';
        line.key =
          from === 'closed' && (to === 'open' || to === 'in_review')
            ? finding
              ? 'actionDetail.history.findingReopenedBy'
              : 'actionDetail.history.reopened'
            : finding
              ? 'actionDetail.history.findingStatus'
              : 'actionDetail.history.status';
        line.params = { who, status: to, entry: entry(event.findingId) };
        line.quote = text(d, 'note');
        break;
      }
      case 'action.note_added':
        line.key = 'actionDetail.history.note';
        line.params = { who };
        line.quote = text(d, 'text');
        break;
      case 'backfill.completed':
        line.key = 'actionDetail.history.backfill';
        line.params = {};
        break;
      case 'finding.joined':
        line.key = 'actionDetail.history.joined';
        line.params = { entry: entry(event.findingId) };
        line.change = [null, priorityOfLevel(d['level'] as number)];
        break;
      case 'finding.closed':
        line.key = 'actionDetail.history.findingClosed';
        line.params = { entry: entry(event.findingId) };
        line.reason = SYSTEM_REASON_LABELS[text(d, 'reason') ?? ''] ?? null;
        break;
      case 'finding.reopened':
        line.key = 'actionDetail.history.findingReopened';
        line.params = { entry: entry(event.findingId) };
        break;
    }
    return line;
  });
}
