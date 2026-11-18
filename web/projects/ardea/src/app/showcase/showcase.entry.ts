import { Routes } from '@angular/router';

/**
 * The component library pages at `/ui`: every `ui` component in all its states, and the base of
 * the visual comparison (master plan 11.7, 13.6). Development builds only: the production build
 * swaps this file for `showcase.entry.prod.ts` (angular.json, fileReplacements), so none of it ships.
 */
export const SHOWCASE_ROUTES: Routes = [
  {
    path: 'ui',
    loadChildren: () => import('./showcase.routes').then((m) => m.SHOWCASE_PAGES),
  },
];
