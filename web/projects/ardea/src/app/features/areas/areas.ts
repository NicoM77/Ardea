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

export interface Area {
  id: AreaId;
  path: string;
  label: string;
  emptyHeading?: string;
  emptyText?: string;
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
    id: 'notices',
    path: 'notices',
    label: 'nav.notices',
    group: 'main',
  },
  {
    id: 'reminders',
    path: 'reminders',
    label: 'nav.reminders',
    group: 'main',
  },
  {
    id: 'vulnerabilities',
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

export const ADMINISTRATION: Area = {
  id: 'administration',
  path: '/admin',
  label: 'nav.administration',
  group: 'foot',
};

export function areaById(id: AreaId): Area {
  return id === 'administration' ? ADMINISTRATION : (AREAS.find((a) => a.id === id) ?? AREAS[0]);
}

export function isInstanceWide(area: Area): boolean {
  return area.path.startsWith('/');
}

export function areaUrl(area: Area, organizationSlug: string | null | undefined): string | null {
  if (isInstanceWide(area)) return area.path;
  return organizationSlug ? `/o/${organizationSlug}/${area.path}` : null;
}

export const AREA_ROUTES: Routes = AREAS.filter(
  (area) => !isInstanceWide(area) && !!area.emptyHeading,
).map((area) => ({
  path: area.path,
  title: area.label,
  data: { area: area.id },
  loadComponent: () => import('./area-page').then((m) => m.AreaPage),
}));
