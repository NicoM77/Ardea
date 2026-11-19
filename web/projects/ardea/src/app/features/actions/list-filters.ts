import type { ListActionsParams } from '../../core/api/client';
import { ACTION_PRIORITIES, LEVEL_OF, type ActionPriority } from './action-format';

/**
 * The filters of the actions list (task 3.9), kept in the address so a filtered list can be
 * shared, survives a reload and is where «back» returns to: `?view=closed&priority=critical,high
 * &project=<id>&q=postgres`. The overview's ring links here with a priority preset.
 */
export interface ActionFilters {
  view: 'open' | 'closed';
  priorities: ActionPriority[];
  project: string;
  q: string;
}

export const NO_ACTION_FILTERS: ActionFilters = {
  view: 'open',
  priorities: [],
  project: '',
  q: '',
};

/** Reads the filters from the address, accepting only values the list knows. */
export function actionFiltersFrom(params: {
  view?: string | null;
  priority?: string | null;
  project?: string | null;
  q?: string | null;
}): ActionFilters {
  const wanted = (params.priority ?? '').split(',').map((p) => p.trim());
  return {
    view: params.view === 'closed' ? 'closed' : 'open',
    // In the list's order, once each, whatever order the address has.
    priorities: ACTION_PRIORITIES.filter((p) => wanted.includes(p)),
    project: /^[0-9a-f-]{36}$/i.test(params.project ?? '') ? params.project!.toLowerCase() : '',
    q: (params.q ?? '').trim(),
  };
}

/** The address of the filters; defaults are left out. */
export function actionFiltersQuery(filters: ActionFilters): Record<string, string | null> {
  return {
    view: filters.view === 'closed' ? 'closed' : null,
    priority: filters.priorities.length ? filters.priorities.join(',') : null,
    project: filters.project || null,
    q: filters.q.trim() || null,
  };
}

/** Whether anything narrows the list beyond the open or closed view. */
export function isFiltered(filters: ActionFilters): boolean {
  return filters.priorities.length > 0 || !!filters.project || !!filters.q;
}

/** The request for the rows: open means open and in review (`active`); levels as numbers. */
export function actionListParams(
  filters: ActionFilters,
  cursor: string | undefined,
  limit = 50,
): ListActionsParams {
  return {
    status: filters.view === 'open' ? 'active' : 'closed',
    ...(filters.priorities.length
      ? { level: filters.priorities.map((p) => LEVEL_OF[p]).join(',') }
      : {}),
    ...(filters.project ? { project: filters.project } : {}),
    ...(filters.q ? { q: filters.q } : {}),
    ...(cursor ? { cursor } : {}),
    limit,
  };
}

/**
 * The request behind the counts on the priority chips, the summary and the owners: the same
 * view, project and words without the priorities, so each chip says how many rows it would leave.
 */
export function actionCountParams(filters: ActionFilters): ListActionsParams {
  return actionListParams({ ...filters, priorities: [] }, undefined, 100);
}

/** Pressing a chip adds or removes its priority. */
export function togglePriority(filters: ActionFilters, priority: ActionPriority): ActionFilters {
  const has = filters.priorities.includes(priority);
  return {
    ...filters,
    priorities: ACTION_PRIORITIES.filter((p) =>
      p === priority ? !has : filters.priorities.includes(p),
    ),
  };
}
