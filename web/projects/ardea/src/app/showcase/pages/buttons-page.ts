import { Component } from '@angular/core';
import { LucideArrowRight, LucideCopy, LucideMenu, LucideSearch } from '@lucide/angular';
import { UiButton, UiCard, UiKbd, UiPageHeader } from 'ui';

@Component({
  selector: 'ardea-buttons-page',
  imports: [
    LucideArrowRight,
    LucideCopy,
    LucideMenu,
    LucideSearch,
    UiButton,
    UiCard,
    UiKbd,
    UiPageHeader,
  ],
  template: `
    <div class="sc-page">
      <ui-page-header
        heading="Buttons"
        sub="32px high, 40px on phones, 6px corners. One primary button per screen."
      />

      @for (v of variants; track v.variant) {
        <ui-card [heading]="v.label">
          <div class="sc-rows">
            <div class="sc-row">
              <span class="sc-label">Default</span>
              <div class="sc-demo">
                <button uiButton [variant]="v.variant" type="button">{{ v.text }}</button>
              </div>
            </div>
            <div class="sc-row">
              <span class="sc-label">Hover</span>
              <div class="sc-demo">
                <button uiButton [variant]="v.variant" type="button" data-ui-state="hover">
                  {{ v.text }}
                </button>
              </div>
            </div>
            <div class="sc-row">
              <span class="sc-label">Focus</span>
              <div class="sc-demo">
                <button uiButton [variant]="v.variant" type="button" data-ui-state="focus">
                  {{ v.text }}
                </button>
              </div>
            </div>
            <div class="sc-row">
              <span class="sc-label">Loading</span>
              <div class="sc-demo">
                <button uiButton [variant]="v.variant" type="button" loading>{{ v.text }}</button>
              </div>
            </div>
            <div class="sc-row">
              <span class="sc-label">Disabled</span>
              <div class="sc-demo">
                <button uiButton [variant]="v.variant" type="button" disabled>{{ v.text }}</button>
              </div>
            </div>
          </div>
        </ui-card>
      }

      <ui-card heading="With icons and as links">
        <div class="sc-rows">
          <div class="sc-row">
            <span class="sc-label">Icon and label</span>
            <div class="sc-demo">
              <button uiButton type="button">
                <svg lucideCopy aria-hidden="true"></svg>Copy key
              </button>
              <a uiButton variant="ghost" href="#"
                >Open in app<svg lucideArrowRight aria-hidden="true"></svg
              ></a>
            </div>
          </div>
          <div class="sc-row">
            <span class="sc-label">Icon only</span>
            <div class="sc-demo">
              <button uiButton variant="ghost" iconOnly type="button" aria-label="Open menu">
                <svg lucideMenu aria-hidden="true"></svg>
              </button>
              <button uiButton iconOnly type="button" aria-label="Search">
                <svg lucideSearch aria-hidden="true"></svg>
              </button>
            </div>
          </div>
          <div class="sc-row">
            <span class="sc-label">Full width</span>
            <div class="sc-demo" style="max-width: 336px">
              <button uiButton variant="primary" block type="button">Sign in</button>
            </div>
          </div>
          <div class="sc-row">
            <span class="sc-label">Key caps</span>
            <div class="sc-demo"><ui-kbd>/</ui-kbd><ui-kbd>Ctrl K</ui-kbd><ui-kbd>Esc</ui-kbd></div>
          </div>
        </div>
      </ui-card>
    </div>
  `,
})
export class ButtonsPage {
  protected readonly variants = [
    { variant: 'primary', label: 'Primary', text: 'Take into review' },
    { variant: 'default', label: 'Default', text: 'Save note' },
    { variant: 'ghost', label: 'Ghost', text: 'Cancel' },
  ] as const;
}
