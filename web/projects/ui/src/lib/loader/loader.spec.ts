import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UiLoader } from './loader';

@Component({
  imports: [UiLoader],
  template: `<ui-loader size="page" [label]="label()" [frame]="frame()" />`,
})
class Host {
  readonly label = signal<string | null>('Loading the overview');
  readonly frame = signal<number | null>(null);
}

describe('UiLoader', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  async function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const host = (fixture.nativeElement as HTMLElement).querySelector('ui-loader')!;
    return { fixture, host };
  }

  it('shows nothing for the first 300ms, so a quick answer never flashes it', async () => {
    const { fixture, host } = await render();
    vi.advanceTimersByTime(299);
    fixture.detectChanges();
    expect(host.querySelector('svg')).toBeNull();

    vi.advanceTimersByTime(1);
    fixture.detectChanges();
    expect(host.querySelector('svg')).not.toBeNull();
    expect(host.textContent?.trim()).toBe('Loading the overview');
  });

  it('is a status region while it has a label, and decorative without one', async () => {
    const { fixture, host } = await render();
    expect(host.getAttribute('role')).toBe('status');
    fixture.componentInstance.label.set(null);
    fixture.detectChanges();
    expect(host.hasAttribute('role')).toBe(false);
  });

  it('draws a still frame: the neck lines by the frame, the crest only at the end', async () => {
    const { fixture, host } = await render();
    fixture.componentInstance.frame.set(0.6);
    vi.advanceTimersByTime(300);
    fixture.detectChanges();
    const offset = (name: string) =>
      (host.querySelector(`.loop .${name}`) as SVGPathElement).style.strokeDashoffset;
    expect(offset('back')).toBe('40');
    expect(offset('crest')).toBe('100');
    expect((host.querySelector('.loop .eye') as SVGCircleElement).style.opacity).toBe('0');
  });
});
