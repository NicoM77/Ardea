import {
  provideHttpClient,
  withFetch,
  withInterceptors,
  withXsrfConfiguration,
} from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import {
  provideRouter,
  TitleStrategy,
  withComponentInputBinding,
  withInMemoryScrolling,
  withViewTransitions,
} from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { provideLucideConfig } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { AuthApi } from './core/api/auth-api';
import { sessionExpiredInterceptor } from './core/http/session-expired.interceptor';
import { provideTranslations } from './core/i18n/translations';
import { TranslatedTitleStrategy } from './core/i18n/title-strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled' }),
      withViewTransitions({ skipInitialTransition: true }),
    ),
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
    // Same origin: the API serves the app in production, the dev server proxies /api.
    provideHttpClient(
      withFetch(),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([sessionExpiredInterceptor]),
    ),
    provideTranslations(),
    // Icons: 1.6px line drawings on a 24px grid, shown at 16px (DESIGN.md, Shapes)
    provideLucideConfig({ size: 16, strokeWidth: 1.6 }),
    provideAppInitializer(() => {
      // The antiforgery cookie must exist before the first write; a failure here is not fatal,
      // the pages say so when their request fails.
      void inject(AuthApi)
        .antiforgery()
        .catch(() => undefined);
      return firstValueFrom(inject(TranslocoService).load('en')).then(() => undefined);
    }),
  ],
};
