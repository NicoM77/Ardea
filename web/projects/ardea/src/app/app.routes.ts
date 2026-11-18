import { Route, Routes, UrlSegment } from '@angular/router';
import {
  guestGuard,
  homeRedirectGuard,
  instanceAdminGuard,
  organizationGuard,
  setupGuard,
  signedInGuard,
} from './core/session/guards';
import { AREA_ROUTES } from './features/areas/areas';
import { SHOWCASE_ROUTES } from './showcase/showcase.entry';

export const routes: Routes = [
  {
    path: '',
    // An empty-path layout would also match `/` with an empty outlet; `/` belongs to the shell.
    canMatch: [(_route: Route, segments: UrlSegment[]) => segments.length > 0],
    loadComponent: () => import('./layout/auth-layout/auth-layout').then((m) => m.AuthLayout),
    children: [
      {
        path: 'setup',
        canActivate: [setupGuard],
        title: 'auth.setup.title',
        loadComponent: () => import('./features/auth/setup-page').then((m) => m.SetupPage),
      },
      {
        path: 'sign-in',
        canActivate: [guestGuard],
        title: 'auth.signIn.title',
        loadComponent: () => import('./features/auth/sign-in-page').then((m) => m.SignInPage),
      },
      {
        path: 'sign-in/two-factor',
        canActivate: [guestGuard],
        title: 'auth.twoFactor.title',
        loadComponent: () => import('./features/auth/two-factor-page').then((m) => m.TwoFactorPage),
      },
      {
        path: 'register',
        canActivate: [guestGuard],
        title: 'auth.register.title',
        loadComponent: () => import('./features/auth/register-page').then((m) => m.RegisterPage),
      },
      {
        path: 'forgot-password',
        canActivate: [guestGuard],
        title: 'auth.forgot.title',
        loadComponent: () =>
          import('./features/auth/forgot-password-page').then((m) => m.ForgotPasswordPage),
      },
      {
        path: 'reset-password',
        canActivate: [guestGuard],
        title: 'auth.reset.title',
        loadComponent: () =>
          import('./features/auth/reset-password-page').then((m) => m.ResetPasswordPage),
      },
      {
        path: 'unavailable',
        title: 'unavailable.title',
        loadComponent: () =>
          import('./features/unavailable/unavailable-page').then((m) => m.UnavailablePage),
      },
    ],
  },
  ...SHOWCASE_ROUTES,
  {
    path: '',
    canActivate: [signedInGuard],
    loadComponent: () => import('./layout/shell/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', canActivate: [homeRedirectGuard], children: [] },
      {
        path: 'no-organization',
        title: 'home.noOrganization.title',
        loadComponent: () =>
          import('./features/home/no-organization-page').then((m) => m.NoOrganizationPage),
      },
      {
        path: 'account/two-factor',
        title: 'account.twoFactor.title',
        loadComponent: () =>
          import('./features/account/enable-two-factor-page').then((m) => m.EnableTwoFactorPage),
      },
      {
        path: 'admin',
        canActivate: [instanceAdminGuard],
        title: 'nav.administration',
        data: { area: 'administration' },
        loadComponent: () => import('./features/areas/area-page').then((m) => m.AreaPage),
      },
      {
        path: 'o/:slug',
        canActivate: [organizationGuard],
        children: [{ path: '', pathMatch: 'full', redirectTo: 'overview' }, ...AREA_ROUTES],
      },
      {
        path: '**',
        title: 'notFound.title',
        loadComponent: () =>
          import('./features/not-found/not-found-page').then((m) => m.NotFoundPage),
      },
    ],
  },
];
