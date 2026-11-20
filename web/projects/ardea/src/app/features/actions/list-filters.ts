import type { ListActionsParams } from '../../core/api/client';
import { ACTION_PRIORITIES, LEVEL_OF, type ActionPriority } from './action-format';

export interface ActionFilters {
  view: 'open' | 'closed';
  priorities: ActionPriority[];
  project: string;
  q: string;
}

export function actionFiltersFrom(params: {
  view?: string | null;
  priority?: string | null;
  project?: string | null;
  q?: string | null;
}): ActionFilters {
  const wanted = (params.priority ?? '').split(',').map((p) => p.trim());
  return {
    view: params.view === 'closed' ? 'closed' : 'open',
    priorities: ACTION_PRIORITIES.filter((p) => wanted.includes(p)),
    project: /^[0-9a-f-]{36}$/i.test(params.project ?? '') ? params.project!.toLowerCase() : '',
    q: (params.q ?? '').trim(),
  };
}

export function actionFiltersQuery(filters: ActionFilters): Record<string, string | null> {
  return {
    view: filters.view === 'closed' ? 'closed' : null,
    priority: filters.priorities.length ? filters.priorities.join(',') : null,
    project: filters.project || null,
    q: filters.q.trim() || null,
  };
}

export function isFiltered(filters: ActionFilters): boolean {
  return filters.priorities.length > 0 || !!filters.project || !!filters.q;
}

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

export function actionCountParams(filters: ActionFilters): ListActionsParams {
  return actionListParams({ ...filters, priorities: [] }, undefined, 100);
}

export function togglePriority(filters: ActionFilters, priority: ActionPriority): ActionFilters {
  const has = filters.priorities.includes(priority);
  return {
    ...filters,
    priorities: ACTION_PRIORITIES.filter((p) =>
      p === priority ? !has : filters.priorities.includes(p),
    ),
  };
}
