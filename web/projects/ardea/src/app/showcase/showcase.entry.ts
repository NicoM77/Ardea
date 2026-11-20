import { Routes } from '@angular/router';

export const SHOWCASE_ROUTES: Routes = [
  {
    path: 'ui',
    loadChildren: () => import('./showcase.routes').then((m) => m.SHOWCASE_PAGES),
  },
];
