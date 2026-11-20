import { TestBed } from '@angular/core/testing';
import { UiToaster } from './toast';

describe('UiToaster', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('keeps the toast on screen for the fade-out after it is dismissed', () => {
    const toaster = TestBed.inject(UiToaster);
    toaster.show({ text: 'Saved' });
    expect(toaster.rendered()?.text).toBe('Saved');
    toaster.dismiss();
    expect(toaster.current()).toBeNull();
    expect(toaster.leaving()).toBe(true);
    expect(toaster.rendered()?.text).toBe('Saved');
    vi.advanceTimersByTime(UiToaster.exit);
    expect(toaster.rendered()).toBeNull();
    expect(toaster.leaving()).toBe(false);
  });

  it('fades out on its own after the display time and is replaced at once by a new toast', () => {
    const toaster = TestBed.inject(UiToaster);
    toaster.show({ text: 'One' });
    vi.advanceTimersByTime(UiToaster.duration);
    expect(toaster.leaving()).toBe(true);
    toaster.show({ text: 'Two' });
    expect(toaster.leaving()).toBe(false);
    vi.advanceTimersByTime(UiToaster.exit);
    expect(toaster.rendered()?.text).toBe('Two');
  });

  it('runs the undo action and fades out', () => {
    const toaster = TestBed.inject(UiToaster);
    const undo = vi.fn();
    toaster.show({ text: 'Gone', undo });
    toaster.undo();
    expect(undo).toHaveBeenCalledOnce();
    expect(toaster.leaving()).toBe(true);
  });
});
