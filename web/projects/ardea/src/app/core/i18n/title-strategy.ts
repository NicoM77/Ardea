import { inject, Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

/** Route titles are translation keys; the document title reads "Sign in · Ardea". */
@Injectable({ providedIn: 'root' })
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly transloco = inject(TranslocoService);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const key = this.buildTitle(snapshot);
    const product = this.transloco.translate('app.name');
    // Keys contain a dot; the development-only component pages use plain titles.
    const page = key?.includes('.') ? this.transloco.translate(key) : key;
    this.title.setTitle(page ? `${page} · ${product}` : product);
  }
}
