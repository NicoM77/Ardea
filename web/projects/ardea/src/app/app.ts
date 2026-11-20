import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  NavigationCancel,
  NavigationEnd,
  NavigationError,
  Router,
  RouterOutlet,
} from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiLoader } from 'ui';
import { filter, map, take } from 'rxjs';

@Component({
  selector: 'ardea-root',
  imports: [RouterOutlet, TranslocoPipe, UiLoader],
  template: `
    @if (!started()) {
      <div class="start" aria-busy="true">
        <ui-loader size="page" [label]="'app.loading' | transloco" />
      </div>
    }
    <router-outlet />
  `,
  styles: `
    .start {
      display: grid;
      place-items: center;
      min-height: 100vh;
      min-height: 100dvh;
    }
  `,
})
export class App {
  protected readonly started = toSignal(
    inject(Router).events.pipe(
      filter(
        (event) =>
          event instanceof NavigationEnd ||
          event instanceof NavigationCancel ||
          event instanceof NavigationError,
      ),
      take(1),
      map(() => true),
    ),
    { initialValue: false },
  );
}
