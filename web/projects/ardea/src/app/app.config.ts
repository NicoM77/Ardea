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
import { apiBaseUrlInterceptor } from './core/http/api-base-url.interceptor';
import { sessionExpiredInterceptor } from './core/http/session-expired.interceptor';
import { Session } from './core/session/session';
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
      withInterceptors([apiBaseUrlInterceptor, sessionExpiredInterceptor]),
    ),
    provideTranslations(),
    // Icons: 1.6px line drawings on a 24px grid, shown at 16px (DESIGN.md, Shapes)
    provideLucideConfig({ size: 16, strokeWidth: 1.6 }),
    provideAppInitializer(() => {
      // Start reading the session at once: it tells the guards who is signed in and sets the
      // XSRF-TOKEN cookie before the first write. A failure is not fatal; the guards show it.
      inject(Session);
      return firstValueFrom(inject(TranslocoService).load('en')).then(() => undefined);
    }),
  ],
};
