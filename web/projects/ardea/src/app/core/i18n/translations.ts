import { HttpClient } from '@angular/common/http';
import { EnvironmentProviders, inject, Injectable, isDevMode } from '@angular/core';
import { provideTransloco, Translation, TranslocoLoader } from '@jsverse/transloco';
import { provideTranslocoMessageformat } from '@jsverse/transloco-messageformat';
import { Observable, of } from 'rxjs';
import en from '../../../i18n/en.json';

export const AVAILABLE_LANGUAGES = ['en'] as const;

/**
 * English is built in, so the first screen never waits for a translation file; every other
 * language is fetched from `/i18n/<lang>.json` when someone switches to it (master plan 11.7).
 */
@Injectable({ providedIn: 'root' })
export class TranslationLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string): Observable<Translation> {
    return lang === 'en' ? of(en) : this.http.get<Translation>(`/i18n/${lang}.json`);
  }
}

/** Transloco, switchable at runtime, with ICU message format for plurals from the start. */
export function provideTranslations(): EnvironmentProviders[] {
  return [
    ...provideTransloco({
      config: {
        availableLangs: [...AVAILABLE_LANGUAGES],
        defaultLang: 'en',
        fallbackLang: 'en',
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
        missingHandler: { logMissingKey: true, useFallbackTranslation: true },
      },
      loader: TranslationLoader,
    }),
    provideTranslocoMessageformat(),
  ];
}
