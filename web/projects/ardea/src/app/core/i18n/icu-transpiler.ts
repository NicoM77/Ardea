import { Injectable } from '@angular/core';
import { DefaultTranspiler, TranspileParams } from '@jsverse/transloco';
import { IntlMessageFormat } from 'intl-messageformat';

@Injectable()
export class IcuTranspiler extends DefaultTranspiler {
  private locale = 'en';
  private readonly cache = new Map<string, IntlMessageFormat>();

  override transpile(params: TranspileParams): unknown {
    const { value } = params;
    const text = super.transpile(params);
    if (typeof value !== 'string' || typeof text !== 'string' || !text.includes('{')) return text;
    try {
      return this.format(text).format(params.params ?? {}) as string;
    } catch {
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
