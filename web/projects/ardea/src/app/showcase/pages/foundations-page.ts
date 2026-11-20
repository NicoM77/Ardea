import { Component } from '@angular/core';
import { UiCard, UiPageHeader } from 'ui';

interface Swatch {
  name: string;
  token: string;
  note: string;
}

@Component({
  selector: 'ardea-foundations-page',
  imports: [UiCard, UiPageHeader],
  template: `
    <div class="sc-page">
      <ui-page-header
        heading="Foundations"
        sub="Tokens from projects/ui/src/styles/tokens.css. Components use nothing else."
      />

      <ui-card heading="Colours">
        <span uiCardMeta>Contrast against the white sheet</span>
        <div class="sc-swatches">
          @for (s of swatches; track s.token) {
            <div class="sc-swatch">
              <i [style.background]="'var(' + s.token + ')'"></i>
              <b>{{ s.name }}</b>
              <span class="id">{{ s.token }}</span>
              <span>{{ s.note }}</span>
            </div>
          }
        </div>
      </ui-card>

      <ui-card heading="Type">
        <span uiCardMeta>IBM Plex Sans, IBM Plex Mono for identifiers</span>
        <div class="sc-rows">
          <div class="sc-row">
            <span class="sc-label">Figure · 36 / 500</span
            ><span class="t-figure">1 d 13 h 29 m</span>
          </div>
          <div class="sc-row">
            <span class="sc-label">Number · 28 / 500</span><span class="t-kpi">6,432</span>
          </div>
          <div class="sc-row">
            <span class="sc-label">Title · 22 / 600</span><span class="t-title">Actions</span>
          </div>
          <div class="sc-row">
            <span class="sc-label">Section · 16 / 600</span><span class="t-section">Findings</span>
          </div>
          <div class="sc-row">
            <span class="sc-label">Body · 14 / 400–500</span
            ><span>Example project runs 2025.9.0, inside the affected range.</span>
          </div>
          <div class="sc-row">
            <span class="sc-label">Sub · 13 / 400</span
            ><span class="t-sub">Updated 4 minutes ago</span>
          </div>
          <div class="sc-row">
            <span class="sc-label">Small · 12 / 400</span
            ><span class="t-small">Webshop · PHP 8.3.30</span>
          </div>
          <div class="sc-row">
            <span class="sc-label">Identifier · mono 13</span
            ><span
              ><span class="id">CVE-2026-115731</span>&nbsp;&nbsp;<span class="id"
                >2025.12.1</span
              ></span
            >
          </div>
        </div>
      </ui-card>

      <ui-card heading="Shape and focus">
        <div class="sc-demo">
          <span class="shape" style="border-radius: var(--radius-sheet)">Sheet 16</span>
          <span class="shape" style="border-radius: var(--radius-dialog)">Dialog 12</span>
          <span class="shape" style="border-radius: var(--radius-card)">Card 8</span>
          <span class="shape" style="border-radius: var(--radius-control)">Control 6</span>
          <span class="shape focus">Focus ring 2px Accent</span>
        </div>
      </ui-card>
    </div>
  `,
  styles: `
    .t-figure {
      font-size: var(--fs-figure);
      line-height: var(--lh-figure);
      font-weight: 500;
      letter-spacing: -0.02em;
    }
    .t-kpi {
      font-size: var(--fs-kpi);
      line-height: var(--lh-kpi);
      font-weight: 500;
      letter-spacing: -0.01em;
    }
    .t-title {
      font-size: var(--fs-title);
      line-height: var(--lh-title);
      font-weight: 600;
    }
    .t-section {
      font-size: var(--fs-section);
      line-height: var(--lh-section);
      font-weight: 600;
    }
    .t-sub {
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    .t-small {
      color: var(--muted);
      font-size: var(--fs-small);
    }
    .shape {
      display: grid;
      place-items: center;
      width: 120px;
      height: 64px;
      border: 1px solid var(--border);
      background: var(--raised);
      color: var(--muted);
      font-size: var(--fs-small);
    }
    .shape.focus {
      width: 160px;
      border-radius: var(--radius-control);
      background: var(--surface);
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
  `,
})
export class FoundationsPage {
  protected readonly swatches: Swatch[] = [
    { name: 'Ground', token: '--ground', note: 'the frame' },
    { name: 'Surface', token: '--surface', note: 'the sheet' },
    { name: 'Raised', token: '--raised', note: 'wells, hover' },
    { name: 'Active', token: '--active', note: 'selected' },
    { name: 'Border', token: '--border', note: 'card edges' },
    { name: 'Control', token: '--control', note: 'button edges' },
    { name: 'Field', token: '--field', note: 'inputs · 3.7:1' },
    { name: 'Text', token: '--text', note: '18.4:1' },
    { name: 'Muted', token: '--muted', note: '7.1:1' },
    { name: 'Faint', token: '--faint', note: '5.4:1' },
    { name: 'Accent', token: '--accent', note: 'data, focus · 6.0:1' },
    { name: 'Critical', token: '--critical', note: '5.9:1' },
    { name: 'High', token: '--high', note: '5.2:1' },
    { name: 'Medium', token: '--medium', note: '5.5:1' },
    { name: 'Low', token: '--low', note: '7.1:1' },
    { name: 'Positive', token: '--positive', note: '5.2:1' },
  ];
}
