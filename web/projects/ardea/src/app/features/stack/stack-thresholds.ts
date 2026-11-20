import { Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

@Component({
  selector: 'ardea-stack-thresholds',
  imports: [TranslocoPipe],
  template: `
    <dl>
      <dt>{{ 'stack.threshold.onlyImportant' | transloco }}</dt>
      <dd>{{ 'stack.explain.onlyImportant' | transloco }}</dd>
      <dt>{{ 'stack.threshold.everything' | transloco }}</dt>
      <dd>{{ 'stack.explain.everything' | transloco }}</dd>
      <dt>{{ 'stack.explain.productTitle' | transloco }}</dt>
      <dd>{{ 'stack.explain.product' | transloco }}</dd>
      <dt>{{ 'stack.explain.versionTitle' | transloco }}</dt>
      <dd>{{ 'stack.explain.version' | transloco }}</dd>
    </dl>
  `,
  styles: `
    dl {
      margin: 0;
    }
    dt {
      font-weight: 500;
    }
    dd {
      margin: 2px 0 var(--space-md);
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    dd:last-child {
      margin-bottom: 0;
    }
  `,
})
export class StackThresholds {}
