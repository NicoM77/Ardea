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
        path: 'confirm-email',
        title: 'auth.confirmEmail.title',
        loadComponent: () =>
          import('./features/auth/confirm-email-page').then((m) => m.ConfirmEmailPage),
      },
      {
        path: 'invitations/:token',
        title: 'invitation.title',
        loadComponent: () =>
          import('./features/invitations/invitation-page').then((m) => m.InvitationPage),
      },
      ...(['confirm', 'unsubscribe', 'resubscribe'] as const).map((mode) => ({
        path: `notifications/${mode}`,
        title: 'notify.link.title',
        data: { mode },
        loadComponent: () =>
          import('./features/notifications/address-link-page').then((m) => m.AddressLinkPage),
      })),
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
        path: 'organizations/new',
        title: 'organizations.create.title',
        loadComponent: () =>
          import('./features/organizations/create-organization-page').then(
            (m) => m.CreateOrganizationPage,
          ),
      },
      {
        path: 'account/two-factor',
        title: 'account.twoFactor.title',
        loadComponent: () =>
          import('./features/account/enable-two-factor-page').then((m) => m.EnableTwoFactorPage),
      },
      {
        path: 'entries',
        title: 'entries.title',
        loadComponent: () => import('./features/entries/entries-page').then((m) => m.EntriesPage),
      },
      {
        path: 'entries/:key',
        title: 'entryDetail.title',
        loadComponent: () => import('./features/entries/entry-page').then((m) => m.EntryPage),
      },
      {
        path: 'sources',
        title: 'sources.title',
        loadComponent: () => import('./features/sources/sources-page').then((m) => m.SourcesPage),
      },
      {
        path: 'admin',
        canActivate: [instanceAdminGuard],
        title: 'nav.administration',
        loadComponent: () => import('./features/admin/admin-page').then((m) => m.AdminPage),
      },
      {
        path: 'o/:slug',
        canActivate: [organizationGuard],
        loadComponent: () =>
          import('./features/organizations/organization-frame').then((m) => m.OrganizationFrame),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'overview' },
          { path: 'vulnerabilities', redirectTo: '/entries' },
          { path: 'sources', redirectTo: '/sources' },
          {
            path: 'overview',
            title: 'nav.overview',
            loadComponent: () =>
              import('./features/overview/overview-page').then((m) => m.OverviewPage),
          },
          {
            path: 'notices',
            title: 'nav.notices',
            loadComponent: () =>
              import('./features/notices/notices-page').then((m) => m.NoticesPage),
          },
          { path: 'actions', redirectTo: 'reminders' },
          { path: 'actions/:number', redirectTo: 'reminders/:number' },
          {
            path: 'reminders',
            title: 'nav.reminders',
            loadComponent: () =>
              import('./features/actions/actions-page').then((m) => m.ActionsPage),
          },
          {
            path: 'reminders/:number',
            title: 'actionDetail.title',
            loadComponent: () => import('./features/actions/action-page').then((m) => m.ActionPage),
          },
          {
            path: 'alerts/:number',
            title: 'alert.title',
            loadComponent: () => import('./features/actions/alert-page').then((m) => m.AlertPage),
          },
          {
            path: 'setup',
            title: 'wizard.title',
            data: { mode: 'organization' },
            loadComponent: () => import('./features/setup/setup-wizard').then((m) => m.SetupWizard),
          },
          {
            path: 'projects',
            title: 'nav.projects',
            loadComponent: () =>
              import('./features/projects/projects-page').then((m) => m.ProjectsPage),
          },
          {
            path: 'stack',
            title: 'nav.stack',
            loadComponent: () => import('./features/stack/stack-page').then((m) => m.StackPage),
          },
          {
            path: 'projects/new',
            title: 'wizard.titleNew',
            data: { mode: 'new' },
            loadComponent: () => import('./features/setup/setup-wizard').then((m) => m.SetupWizard),
          },
          {
            path: 'projects/:projectId',
            title: 'projects.title',
            loadComponent: () =>
              import('./features/projects/project-page').then((m) => m.ProjectPage),
          },
          {
            path: 'projects/:projectId/setup',
            title: 'wizard.titleProject',
            data: { mode: 'project' },
            loadComponent: () => import('./features/setup/setup-wizard').then((m) => m.SetupWizard),
          },
          {
            path: 'settings',
            title: 'nav.settings',
            loadComponent: () =>
              import('./features/settings/settings-page').then((m) => m.SettingsPage),
          },
          {
            path: 'settings/notifications',
            title: 'notify.settings.title',
            loadComponent: () =>
              import('./features/notifications/notification-settings-page').then(
                (m) => m.NotificationSettingsPage,
              ),
          },
          {
            path: 'notifications',
            title: 'notify.inbox.title',
            loadComponent: () =>
              import('./features/notifications/inbox-page').then((m) => m.InboxPage),
          },
          {
            path: 'notification-rules',
            title: 'nav.rules',
            loadComponent: () =>
              import('./features/notifications/rules-page').then((m) => m.RulesPage),
          },
          {
            path: 'notification-rules/recipients',
            title: 'notify.tabs.recipients',
            loadComponent: () =>
              import('./features/notifications/recipients-page').then((m) => m.RecipientsPage),
          },
          {
            path: 'notification-rules/log',
            title: 'notify.tabs.log',
            loadComponent: () =>
              import('./features/notifications/delivery-log-page').then((m) => m.DeliveryLogPage),
          },
          {
            path: 'notification-rules/new',
            title: 'notify.editor.titleNew',
            loadComponent: () =>
              import('./features/notifications/rule-editor-page').then((m) => m.RuleEditorPage),
          },
          {
            path: 'notification-rules/:ruleId',
            title: 'notify.editor.titleEdit',
            loadComponent: () =>
              import('./features/notifications/rule-editor-page').then((m) => m.RuleEditorPage),
          },
          {
            path: 'team',
            title: 'nav.team',
            loadComponent: () => import('./features/team/team-page').then((m) => m.TeamPage),
          },
          ...AREA_ROUTES,
        ],
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
