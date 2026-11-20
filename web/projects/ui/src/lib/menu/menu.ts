import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import type { ConnectedPosition } from '@angular/cdk/overlay';
import {
  booleanAttribute,
  Directive,
  effect,
  ElementRef,
  inject,
  input,
  signal,
} from '@angular/core';

export type UiMenuAlign = 'start' | 'end';

const below = (x: 'start' | 'end'): ConnectedPosition => ({
  originX: x,
  originY: 'bottom',
  overlayX: x,
  overlayY: 'top',
  offsetY: 4,
});
const above = (x: 'start' | 'end'): ConnectedPosition => ({
  originX: x,
  originY: 'top',
  overlayX: x,
  overlayY: 'bottom',
  offsetY: -4,
});

const POSITIONS: Record<UiMenuAlign, ConnectedPosition[]> = {
  end: [below('end'), above('end'), below('start'), above('start')],
  start: [below('start'), above('start'), below('end'), above('end')],
};

const OPENING_KEYS = new Set(['Enter', ' ', 'ArrowDown', 'ArrowUp']);

let nextId = 0;

@Directive({
  selector: '[uiMenuTrigger]',
  hostDirectives: [{ directive: CdkMenuTrigger, inputs: ['cdkMenuTriggerFor: uiMenuTrigger'] }],
  host: {
    '(keydown)': 'keyboard($event)',
    '(pointerdown)': 'byKeyboard.set(false)',
  },
})
export class UiMenuTrigger {
  readonly uiMenuAlign = input<UiMenuAlign>('end');
  readonly byKeyboard = signal(false);
  readonly id: string;

  constructor() {
    const element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    element.id ||= `ui-menu-trigger-${nextId++}`;
    this.id = element.id;
    const trigger = inject(CdkMenuTrigger, { self: true });
    trigger.transformOriginSelector = '.ui-menu';
    effect(() => (trigger.menuPosition = POSITIONS[this.uiMenuAlign()]));
  }

  protected keyboard(event: KeyboardEvent): void {
    if (OPENING_KEYS.has(event.key)) this.byKeyboard.set(true);
  }
}

@Directive({
  selector: '[uiMenu]',
  hostDirectives: [CdkMenu],
  host: {
    class: 'ui-menu is-anchored',
    '[class.is-instant]': 'trigger?.byKeyboard()',
    '[attr.aria-labelledby]': 'trigger?.id ?? null',
  },
})
export class UiMenu {
  protected readonly trigger = inject(UiMenuTrigger, { optional: true });
}

@Directive({
  selector: 'button[uiMenuItem], a[uiMenuItem]',
  hostDirectives: [{ directive: CdkMenuItem, outputs: ['cdkMenuItemTriggered: triggered'] }],
  host: {
    class: 'ui-menu-item',
    '[class.is-destructive]': 'destructive()',
  },
})
export class UiMenuItem {
  readonly destructive = input(false, { transform: booleanAttribute });
}
