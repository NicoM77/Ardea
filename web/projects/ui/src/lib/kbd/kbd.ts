import { Component } from '@angular/core';

@Component({
  selector: 'ui-kbd',
  template: `<kbd><ng-content /></kbd>`,
  styles: `
    kbd {
      display: inline-grid;
      place-items: center;
      min-width: 20px;
      height: 20px;
      padding: 0 5px;
      border-radius: var(--radius-key);
      background: var(--raised);
      color: var(--muted);
      font: 500 var(--fs-small) / 1 var(--font);
      white-space: nowrap;
    }
  `,
})
export class UiKbd {}
