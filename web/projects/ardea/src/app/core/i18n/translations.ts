import { HttpClient } from '@angular/common/http';
import {
  EnvironmentProviders,
  inject,
  Injectable,
  isDevMode,
  makeEnvironmentProviders,
} from '@angular/core';
import {
  provideTransloco,
  Translation,
  TRANSLOCO_TRANSPILER,
  TranslocoLoader,
} from '@jsverse/transloco';
import { IcuTranspiler } from './icu-transpiler';
import { Observable, of } from 'rxjs';
import en from '../../../i18n/en.json';

export const AVAILABLE_LANGUAGES = ['en'] as const;

@Injectable({ providedIn: 'root' })
export class TranslationLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string): Observable<Translation> {
    return lang === 'en' ? of(en) : this.http.get<Translation>(`/i18n/${lang}.json`);
  }
}

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
    makeEnvironmentProviders([{ provide: TRANSLOCO_TRANSPILER, useClass: IcuTranspiler }]),
  ];
}
