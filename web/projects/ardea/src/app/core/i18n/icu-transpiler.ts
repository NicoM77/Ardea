import { Injectable } from '@angular/core';
import { DefaultTranspiler, TranspileParams } from '@jsverse/transloco';
import { IntlMessageFormat } from 'intl-messageformat';

/**
 * ICU message format for Transloco (plurals, selects, `{name}` arguments), on FormatJS's
 * intl-messageformat. It interprets the parsed message instead of compiling it to a function,
 * so it runs under the instance's Content Security Policy without `unsafe-eval`; the earlier
 * @messageformat/core compiled every message with `new Function` and failed there.
 */
@Injectable()
export class IcuTranspiler extends DefaultTranspiler {
  private locale = 'en';
  private readonly cache = new Map<string, IntlMessageFormat>();

  override transpile(params: TranspileParams): unknown {
    const { value } = params;
    // Objects and arrays (translateObject) and `{{ key }}` references keep Transloco's handling.
    const text = super.transpile(params);
    if (typeof value !== 'string' || typeof text !== 'string' || !text.includes('{')) return text;
    try {
      return this.format(text).format(params.params ?? {}) as string;
    } catch {
      // A message that is not valid ICU, or misses an argument, shows as written, not as an error.
      return text;
    }
  }

  onLangChanged(lang: string): void {
    this.locale = lang;
    this.cache.clear();
  }

  private format(text: string): IntlMessageFormat {
    let message = this.cache.get(text);
    if (!message) {
      message = new IntlMessageFormat(text, this.locale);
      this.cache.set(text, message);
    }
    return message;
  }
}
