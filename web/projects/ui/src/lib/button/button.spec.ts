import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UiButton } from './button';

@Component({
  imports: [UiButton],
  template: `<button uiButton type="button" [loading]="loading()" (click)="clicks = clicks + 1">
    Save
  </button>`,
})
class Host {
  readonly loading = signal(false);
  clicks = 0;
}

describe('UiButton', () => {
  async function render(loading: boolean) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.loading.set(loading);
    await fixture.whenStable();
    return { fixture, button: (fixture.nativeElement as HTMLElement).querySelector('button')! };
  }

  it('ignores clicks while loading, so a request is not sent twice', async () => {
    const { fixture, button } = await render(true);
    button.click();
    expect(fixture.componentInstance.clicks).toBe(0);
    expect(button.getAttribute('aria-busy')).toBe('true');
  });

  it('stays focusable while loading, so the keyboard keeps its place', async () => {
    const { button } = await render(true);
    expect(button.disabled).toBe(false);
    button.focus();
    expect(document.activeElement).toBe(button);
  });

  it('passes clicks through when idle', async () => {
    const { fixture, button } = await render(false);
    button.click();
    expect(fixture.componentInstance.clicks).toBe(1);
    expect(button.hasAttribute('aria-busy')).toBe(false);
  });
});
