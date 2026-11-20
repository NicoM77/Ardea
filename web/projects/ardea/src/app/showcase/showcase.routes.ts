import { Routes } from '@angular/router';
import { ShowcaseLayout } from './showcase-layout';

export const SHOWCASE_PAGES: Routes = [
  {
    path: '',
    component: ShowcaseLayout,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'foundations' },
      {
        path: 'foundations',
        title: 'Foundations',
        loadComponent: () => import('./pages/foundations-page').then((m) => m.FoundationsPage),
      },
      {
        path: 'buttons',
        title: 'Buttons',
        loadComponent: () => import('./pages/buttons-page').then((m) => m.ButtonsPage),
      },
      {
        path: 'fields',
        title: 'Fields',
        loadComponent: () => import('./pages/fields-page').then((m) => m.FieldsPage),
      },
      {
        path: 'feedback',
        title: 'Feedback',
        loadComponent: () => import('./pages/feedback-page').then((m) => m.FeedbackPage),
      },
      {
        path: 'signals',
        title: 'Priority and status',
        loadComponent: () => import('./pages/signals-page').then((m) => m.SignalsPage),
      },
      {
        path: 'two-factor',
        title: 'Two-factor',
        loadComponent: () => import('./pages/two-factor-page').then((m) => m.TwoFactorShowcasePage),
      },
      {
        path: 'shell',
        title: 'Shell',
        loadComponent: () => import('./pages/shell-page').then((m) => m.ShellShowcasePage),
      },
    ],
  },
];
