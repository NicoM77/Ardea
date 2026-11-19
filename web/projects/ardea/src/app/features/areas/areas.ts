import { Routes } from '@angular/router';
import {
  LucideBell,
  LucideFolder,
  LucideLayoutGrid,
  LucideListChecks,
  LucideRadioTower,
  LucideServerCog,
  LucideShieldExclamationPoint,
  LucideSlidersHorizontal,
  LucideUsers,
  type LucideIconData,
} from '@lucide/angular';

export type AreaId =
  | 'overview'
  | 'actions'
  | 'vulnerabilities'
  | 'projects'
  | 'sources'
  | 'rules'
  | 'team'
  | 'settings'
  | 'administration';

/** A destination in the navigation (master plan 11.4) with what its page says while it is empty. */
export interface Area {
  id: AreaId;
  /** Below `/o/:slug`, or absolute for instance-wide areas. */
  path: string;
  label: string;
  icon: LucideIconData;
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
    icon: LucideLayoutGrid.icon,
    emptyHeading: 'areas.overview.emptyHeading',
    emptyText: 'areas.overview.emptyText',
    group: 'main',
  },
  {
    id: 'actions',
    path: 'actions',
    label: 'nav.actions',
    icon: LucideListChecks.icon,
    emptyHeading: 'areas.actions.emptyHeading',
    emptyText: 'areas.actions.emptyText',
    group: 'main',
  },
  {
    id: 'vulnerabilities',
    // Every consolidated entry, the same for all organizations (master plan 10.2): instance-wide.
    path: '/entries',
    label: 'nav.vulnerabilities',
    icon: LucideShieldExclamationPoint.icon,
    group: 'main',
  },
  {
    id: 'projects',
    path: 'projects',
    label: 'nav.projects',
    icon: LucideFolder.icon,
    emptyHeading: 'areas.projects.emptyHeading',
    emptyText: 'areas.projects.emptyText',
    group: 'main',
  },
  {
    id: 'sources',
    path: '/sources',
    label: 'nav.sources',
    icon: LucideRadioTower.icon,
    emptyHeading: 'areas.sources.emptyHeading',
    emptyText: 'areas.sources.emptyText',
    group: 'main',
  },
  {
    id: 'rules',
    path: 'notification-rules',
    label: 'nav.rules',
    icon: LucideBell.icon,
    emptyHeading: 'areas.rules.emptyHeading',
    emptyText: 'areas.rules.emptyText',
    group: 'main',
  },
  {
    id: 'team',
    path: 'team',
    label: 'nav.team',
    icon: LucideUsers.icon,
    group: 'foot',
  },
  {
    id: 'settings',
    path: 'settings',
    label: 'nav.settings',
    icon: LucideSlidersHorizontal.icon,
    emptyHeading: 'areas.settings.emptyHeading',
    emptyText: 'areas.settings.emptyText',
    group: 'foot',
  },
];

/** Instance-wide, for the instance administrator only; not below an organization. */
export const ADMINISTRATION: Area = {
  id: 'administration',
  path: '/admin',
  label: 'nav.administration',
  icon: LucideServerCog.icon,
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
