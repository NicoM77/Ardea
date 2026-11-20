import {
  booleanAttribute,
  Component,
  contentChild,
  ElementRef,
  input,
  TemplateRef,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideEllipsis } from '@lucide/angular';
import { UiButton } from '../button/button';
import { UiMenuTrigger, type UiMenuAlign } from './menu';

@Component({
  selector: 'ui-overflow-menu',
  imports: [LucideEllipsis, TranslocoPipe, UiButton, UiMenuTrigger],
  template: `
    <button
      #trigger
      uiButton
      variant="ghost"
      iconOnly
      type="button"
      [uiMenuTrigger]="menu()"
      [uiMenuAlign]="align()"
      [loading]="busy()"
      [attr.aria-label]="'ui.menu.more' | transloco: { name: name() }"
    >
      <svg lucideEllipsis aria-hidden="true"></svg>
    </button>
  `,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
    }
  `,
})
export class UiOverflowMenu {
  readonly name = input.required<string>();
  readonly busy = input(false, { transform: booleanAttribute });
  readonly align = input<UiMenuAlign>('end');

  protected readonly menu = contentChild.required(TemplateRef);
  private readonly trigger = viewChild.required('trigger', { read: ElementRef<HTMLButtonElement> });

  focus(): void {
    this.trigger().nativeElement.focus();
  }
}
