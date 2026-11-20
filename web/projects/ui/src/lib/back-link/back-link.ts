import { Location } from '@angular/common';
import { booleanAttribute, Component, DestroyRef, ElementRef, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { LucideChevronLeft } from '@lucide/angular';

@Component({
  selector: 'ui-back-link',
  imports: [LucideChevronLeft, RouterLink],
  template: `
    <a [routerLink]="link()"><svg lucideChevronLeft aria-hidden="true"></svg>{{ label() }}</a>
  `,
  styles: `
    :host {
      display: flex;
      margin: -4px 0 var(--space-sm) -4px;
    }
    a {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      height: 24px;
      padding: 0 6px 0 2px;
      border-radius: var(--radius-control);
      color: var(--muted);
      text-decoration: none;
    }
    a:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    svg {
      flex: none;
    }
    @media (hover: hover) and (pointer: fine) {
      a:hover {
        color: var(--text);
      }
    }
  `,
})
export class UiBackLink {
  readonly label = input.required<string>();
  readonly link = input.required<string | readonly unknown[]>();
  readonly history = input(false, { transform: booleanAttribute });

  private readonly router = inject(Router);
  private readonly location = inject(Location);

  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const back = (event: MouseEvent) => {
      if (!this.goesBack(event)) return;
      event.preventDefault();
      event.stopPropagation();
      this.location.back();
    };
    host.addEventListener('click', back, { capture: true });
    inject(DestroyRef).onDestroy(() => host.removeEventListener('click', back, { capture: true }));
  }

  private goesBack(event: MouseEvent): boolean {
    if (!this.history()) return false;
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)
      return false;
    return !!this.router.lastSuccessfulNavigation()?.previousNavigation;
  }
}
