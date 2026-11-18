import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { UiField } from './field';
import { UiInput } from './input';

@Component({
  imports: [UiField, UiInput],
  template: `
    <ui-field label="Email" [hint]="hint()" [error]="error()">
      <input uiInput type="email" />
    </ui-field>
  `,
})
class Host {
  readonly hint = signal<string | null>('We never share it.');
  readonly error = signal<string | null>(null);
}

describe('UiField', () => {
  async function render() {
    TestBed.configureTestingModule({
      imports: [Host, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
    });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const el: HTMLElement = fixture.nativeElement;
    return {
      fixture,
      input: el.querySelector('input')!,
      label: el.querySelector('label')!,
      text: (id: string) => el.querySelector(`#${id}`)?.textContent?.trim(),
    };
  }

  it('labels the control and points it at the hint', async () => {
    const { input, label, text } = await render();
    expect(label.htmlFor).toBe(input.id);
    expect(text(input.getAttribute('aria-describedby')!)).toBe('We never share it.');
    expect(input.hasAttribute('aria-invalid')).toBe(false);
  });

  it('adds the error to the description and marks the control invalid', async () => {
    const { fixture, input, text } = await render();
    fixture.componentInstance.error.set('Enter an email address.');
    await fixture.whenStable();

    const [hintId, errorId] = input.getAttribute('aria-describedby')!.split(' ');
    expect(text(hintId)).toBe('We never share it.');
    expect(text(errorId)).toBe('Enter an email address.');
    expect(input.getAttribute('aria-invalid')).toBe('true');
  });

  it('drops the error again once it is fixed', async () => {
    const { fixture, input } = await render();
    fixture.componentInstance.hint.set(null);
    fixture.componentInstance.error.set('Enter an email address.');
    await fixture.whenStable();
    fixture.componentInstance.error.set(null);
    await fixture.whenStable();

    expect(input.hasAttribute('aria-describedby')).toBe(false);
    expect(input.hasAttribute('aria-invalid')).toBe(false);
  });
});
