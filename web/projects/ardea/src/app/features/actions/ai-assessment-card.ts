import { Component, computed, inject, input, output } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiButton, UiCard } from 'ui';
import type { ModelAnswersView } from '../../core/api/client';
import { webUrl } from '../entries/entry-detail-format';
import { budget, refs } from './model-format';

@Component({
  selector: 'ardea-ai-assessment-card',
  imports: [TranslocoPipe, UiButton, UiCard],
  template: `
    @let m = model();
    <ui-card [heading]="'aiAssessment.title' | transloco" headingId="ai-assessment">
      <span uiCardMeta class="ai-tag">{{ 'aiAssessment.engine' | transloco }}</span>
      <div class="parts">
        @if (m.preconditions.length) {
          <div>
            <h3 class="h3">{{ 'aiAssessment.preconditions' | transloco }}</h3>
            <ul class="facts">
              @for (p of m.preconditions; track $index) {
                <li>
                  <span [class]="'state state-' + p.state">{{
                    'aiAssessment.state' | transloco: { state: p.state }
                  }}</span>
                  <span>{{ p.condition }}</span>
                </li>
              }
            </ul>
          </div>
        }
        <div>
          <h3 class="h3">{{ 'aiAssessment.impact' | transloco }}</h3>
          <p>{{ m.impact }}</p>
          <p class="muted">
            {{
              'aiAssessment.likelihood'
                | transloco: { likelihood: m.likelihood, confidence: m.confidence }
            }}
            {{ m.likelihoodReason }}
          </p>
        </div>
        @if (m.reasoning.length) {
          <div>
            <h3 class="h3">{{ 'aiAssessment.why' | transloco }}</h3>
            <ul class="steps">
              @for (r of m.reasoning; track $index) {
                <li>
                  <span
                    >{{ r.sentence }}
                    @for (n of numbers(r.sources); track n) {
                      &ngsp;<span class="ref"
                        ><span class="sr-only">{{
                          'aiAssessment.source' | transloco: { number: n }
                        }}</span
                        ><span aria-hidden="true">{{ n }}</span></span
                      >
                    }
                  </span>
                </li>
              }
            </ul>
          </div>
        }
        @if (m.unknowns.length) {
          <div>
            <h3 class="h3">{{ 'aiAssessment.unknowns' | transloco }}</h3>
            <ul class="unknowns">
              @for (u of m.unknowns; track $index) {
                <li>{{ u }}</li>
              }
            </ul>
          </div>
        }
        <div>
          <h3 class="h3">{{ 'aiAssessment.sources' | transloco }}</h3>
          <ol class="sources">
            @for (s of m.sources; track s.number) {
              <li>
                <span class="n">{{ s.number }}.</span>
                @if (web(s.url); as href) {
                  <a class="link" [href]="href" target="_blank" rel="noopener noreferrer">{{
                    s.name ?? s.url
                  }}</a>
                } @else {
                  <span>{{ s.name ?? s.url ?? ('aiAssessment.unnamed' | transloco) }}</span>
                }
              </li>
            }
          </ol>
        </div>
        <div class="budget">
          <span>{{
            'aiAssessment.made'
              | transloco
                : {
                    model: m.model ?? '–',
                    provider: m.provider ?? '–',
                    prompt: m.promptVersion,
                    ruleset: rulesetVersion(),
                    when: when(),
                  }
          }}</span>
          <span>{{
            'aiAssessment.budget'
              | transloco
                : {
                    tokensIn: figures().tokensIn,
                    tokensOut: figures().tokensOut,
                    seconds: figures().seconds,
                  }
          }}</span>
          <span>{{
            (m.requestedByPerson ? 'aiAssessment.byPerson' : 'aiAssessment.bySystem') | transloco
          }}</span>
          @if (canRequest()) {
            <span>
              <button
                uiButton
                variant="ghost"
                type="button"
                [loading]="busy()"
                (click)="again.emit()"
              >
                {{ 'aiAssessment.again' | transloco }}
              </button>
            </span>
          }
        </div>
      </div>
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .ai-tag {
      display: inline-flex;
      align-items: center;
      height: 20px;
      padding: 0 6px;
      border-radius: 4px;
      background: var(--accent-soft);
      color: var(--text);
      font-size: var(--fs-small);
      font-weight: 500;
      white-space: nowrap;
    }
    .parts {
      display: grid;
      gap: var(--space-lg);
    }
    .h3 {
      margin-bottom: var(--space-xs);
      font-size: var(--fs-body);
      font-weight: 500;
    }
    p,
    li {
      overflow-wrap: anywhere;
    }
    .muted,
    .unknowns {
      color: var(--muted);
    }
    .muted {
      margin-top: var(--space-xs);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .facts {
      display: grid;
      gap: 6px;
    }
    .facts li {
      display: grid;
      grid-template-columns: 96px minmax(0, 1fr);
      gap: 10px;
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .state-met {
      font-weight: 500;
    }
    .state-not_met,
    .state-unknown {
      color: var(--muted);
    }
    .steps,
    .unknowns,
    .sources {
      display: grid;
      gap: 4px;
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .unknowns {
      padding-left: 18px;
      list-style: disc;
    }
    .sources li {
      display: flex;
      gap: 6px;
    }
    .n {
      color: var(--muted);
    }
    .ref {
      display: inline-grid;
      place-items: center;
      min-width: 16px;
      height: 16px;
      margin-left: 2px;
      padding: 0 3px;
      border-radius: 3px;
      background: var(--raised);
      color: var(--muted);
      font-size: 11px;
      line-height: 1;
      vertical-align: 1px;
    }
    .budget {
      display: grid;
      gap: 4px;
      padding-top: var(--space-md);
      border-top: 1px solid var(--rule);
      color: var(--muted);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
  `,
})
export class AiAssessmentCard {
  readonly model = input.required<ModelAnswersView>();
  readonly canRequest = input(false);
  readonly busy = input(false);
  readonly again = output();

  private readonly transloco = inject(TranslocoService);
  protected readonly numbers = refs;
  protected readonly web = webUrl;
  protected readonly figures = computed(() => budget(this.model()));
  protected readonly rulesetVersion = computed(() => String(this.model().rulesetVersion));
  protected readonly when = computed(() =>
    new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(this.model().createdAt)),
  );
}
