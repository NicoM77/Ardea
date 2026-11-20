import { Routes } from '@angular/router';

export type AreaId =
  | 'overview'
  | 'notices'
  | 'reminders'
  | 'vulnerabilities'
  | 'projects'
  | 'stack'
  | 'sources'
  | 'rules'
  | 'team'
  | 'settings'
  | 'administration';

/**
 * A destination in the navigation (master plan 11.4) with what its page says while it is empty. Its icon
 * is in the area icons module: this file is read at start-up (the routes), the icons only with the shell.
 */
export interface Area {
  id: AreaId;
  /** Below `/o/:slug`, or absolute for instance-wide areas. */
  path: string;
  label: string;
  emptyHeading?: string;
  emptyText?: string;
  /** Main list or the group at the foot of the sidebar. */
  group: 'main' | 'foot';
}

export const AREAS: readonly Area[] = [
  {
    id: 'overview',
    path: 'overview',
    label: 'nav.overview',
    group: 'main',
  },
  {
    // The inbox «could affect you» of the stack (master plan 17.7, task 5a.5); its count is the queue.
    id: 'notices',
    path: 'notices',
    label: 'nav.notices',
    group: 'main',
  },
  {
    // Reminders without owner or deadline (master plan 17.5); code and API keep «action» (ADR 046).
    id: 'reminders',
    path: 'reminders',
    label: 'nav.reminders',
    group: 'main',
  },
  {
    id: 'vulnerabilities',
    // Every consolidated entry, the same for all organizations (master plan 10.2): instance-wide.
    path: '/entries',
    label: 'nav.vulnerabilities',
    group: 'main',
  },
  {
    id: 'projects',
    path: 'projects',
    label: 'nav.projects',
    group: 'main',
  },
  {
    // The organization's stack (master plan 17.2, task 5a.4): what it runs without projects.
    id: 'stack',
    path: 'stack',
    label: 'nav.stack',
    group: 'main',
  },
  {
    id: 'sources',
    path: '/sources',
    label: 'nav.sources',
    emptyHeading: 'areas.sources.emptyHeading',
    emptyText: 'areas.sources.emptyText',
    group: 'main',
  },
  {
    id: 'rules',
    path: 'notification-rules',
    label: 'nav.rules',
    group: 'main',
  },
  {
    id: 'team',
    path: 'team',
    label: 'nav.team',
    group: 'foot',
  },
  {
    id: 'settings',
    path: 'settings',
    label: 'nav.settings',
    group: 'foot',
  },
];

/** Instance-wide, for the instance administrator only; not below an organization. */
export const ADMINISTRATION: Area = {
  id: 'administration',
  path: '/admin',
  label: 'nav.administration',
  group: 'foot',
};

export function areaById(id: AreaId): Area {
  return id === 'administration' ? ADMINISTRATION : (AREAS.find((a) => a.id === id) ?? AREAS[0]);
}

/** Whether an area lives below `/o/:slug` or is the same for the whole instance. */
export function isInstanceWide(area: Area): boolean {
  return area.path.startsWith('/');
}

/** The address of an area: absolute for instance-wide areas, below the organization otherwise. */
export function areaUrl(area: Area, organizationSlug: string | null | undefined): string | null {
  if (isInstanceWide(area)) return area.path;
  return organizationSlug ? `/o/${organizationSlug}/${area.path}` : null;
}

/**
 * The organization's pages below `/o/:slug` that are still placeholders: their empty state says
 * what will appear. A built page (Team) has its own route and no empty state here.
 */
export const AREA_ROUTES: Routes = AREAS.filter(
  (area) => !isInstanceWide(area) && !!area.emptyHeading,
).map((area) => ({
  path: area.path,
  title: area.label,
  data: { area: area.id },
  loadComponent: () => import('./area-page').then((m) => m.AreaPage),
}));
