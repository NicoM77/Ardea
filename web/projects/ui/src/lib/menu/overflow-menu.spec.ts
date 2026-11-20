import { Component, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { UiMenu, UiMenuItem } from './menu';
import { UiOverflowMenu } from './overflow-menu';

@Component({
  imports: [UiMenu, UiMenuItem, UiOverflowMenu],
  template: `
    <ui-overflow-menu name="Customer Portal" [busy]="busy()">
      <ng-template>
        <div uiMenu>
          <button uiMenuItem type="button" (triggered)="chosen.push('edit')">Edit</button>
          <button uiMenuItem destructive type="button" (triggered)="chosen.push('archive')">
            Archive
          </button>
        </div>
      </ng-template>
    </ui-overflow-menu>
  `,
})
class Host {
  readonly busy = signal(false);
  readonly menu = viewChild.required(UiOverflowMenu);
  readonly chosen: string[] = [];
}

const KEY_CODES: Record<string, number> = {
  Enter: 13,
  Escape: 27,
  ' ': 32,
  ArrowUp: 38,
  ArrowDown: 40,
};

function press(target: Element, key: string): void {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
  Object.defineProperty(event, 'keyCode', { get: () => KEY_CODES[key] });
  target.dispatchEvent(event);
}

describe('UiOverflowMenu', () => {
  async function render() {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: { ui: { menu: { more: 'More actions for {{name}}' } } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
    });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const trigger = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    const panel = () => document.querySelector<HTMLElement>('.ui-menu');
    const items = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')];
    return { fixture, trigger, panel, items };
  }

  afterEach(() => document.querySelector('.cdk-overlay-container')?.replaceChildren());

  it('names the object it acts on, and says it opens a menu', async () => {
    const { trigger } = await render();
    expect(trigger.getAttribute('aria-label')).toBe('More actions for Customer Portal');
    expect(trigger.getAttribute('aria-haspopup')).toBe('menu');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
  });

  it('opens on click, with its items labelled by the trigger and the pop from the trigger', async () => {
    const { fixture, trigger, panel, items } = await render();
    trigger.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    trigger.click();
    await fixture.whenStable();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(panel()?.getAttribute('role')).toBe('menu');
    expect(panel()?.getAttribute('aria-labelledby')).toBe(trigger.id);
    expect(panel()?.classList).not.toContain('is-instant');
    expect(items().map((item) => item.textContent?.trim())).toEqual(['Edit', 'Archive']);
    expect(items()[1].classList).toContain('is-destructive');
  });

  it('opens with the arrow key on the first item, without an entrance, and moves with the arrows', async () => {
    const { fixture, trigger, panel, items } = await render();
    trigger.focus();
    press(trigger, 'ArrowDown');
    await fixture.whenStable();
    expect(panel()?.classList).toContain('is-instant');
    expect(document.activeElement).toBe(items()[0]);
    press(items()[0], 'ArrowDown');
    expect(document.activeElement).toBe(items()[1]);
  });

  it('closes with Escape and gives the focus back to the trigger', async () => {
    const { fixture, trigger, panel, items } = await render();
    trigger.focus();
    press(trigger, 'ArrowDown');
    await fixture.whenStable();
    press(items()[0], 'Escape');
    await fixture.whenStable();
    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('only emits the chosen item, closes and returns the focus to the trigger', async () => {
    const { fixture, trigger, panel, items } = await render();
    trigger.focus();
    press(trigger, 'ArrowDown');
    await fixture.whenStable();
    items()[1].click();
    await fixture.whenStable();
    expect(fixture.componentInstance.chosen).toEqual(['archive']);
    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('shows the work in progress on the trigger and takes the focus back on request', async () => {
    const { fixture, trigger } = await render();
    fixture.componentInstance.busy.set(true);
    await fixture.whenStable();
    expect(trigger.getAttribute('aria-busy')).toBe('true');
    fixture.componentInstance.menu().focus();
    expect(document.activeElement).toBe(trigger);
  });
});
