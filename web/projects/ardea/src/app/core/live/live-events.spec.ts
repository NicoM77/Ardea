import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LIVE_SOURCE, LiveEvents, reloadOnLiveChange, type LiveSource } from './live-events';

class FakeSource implements LiveSource {
  readyState = 0;
  closed = false;
  private readonly listeners = new Map<string, (() => void)[]>();
  constructor(readonly url: string) {}
  addEventListener(type: string, listener: () => void): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }
  close(): void {
    this.closed = true;
    this.readyState = 2;
  }
  emit(type: string): void {
    for (const listener of this.listeners.get(type) ?? []) listener();
  }
}

describe('LiveEvents', () => {
  let sources: FakeSource[];

  beforeEach(() => {
    sources = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: LIVE_SOURCE,
          useValue: (url: string) => {
            const source = new FakeSource(url);
            sources.push(source);
            return source;
          },
        },
      ],
    });
  });
  afterEach(() => vi.useRealTimers());

  it('opens the stream of the organization and counts changed and resync, not ping', () => {
    const live = TestBed.inject(LiveEvents);
    live.connect('org-1');
    expect(sources.map((s) => s.url)).toEqual(['/api/v1/orgs/org-1/events']);

    sources[0].emit('ready');
    sources[0].emit('ping');
    expect(live.changes()).toBe(0);
    sources[0].emit('changed');
    sources[0].emit('resync');
    expect(live.changes()).toBe(2);
  });

  it('counts a ready after a reconnect, because signals may have been lost in between', () => {
    const live = TestBed.inject(LiveEvents);
    live.connect('org-1');
    sources[0].emit('ready');
    sources[0].emit('ready');
    expect(live.changes()).toBe(1);
  });

  it('opens a stream the browser gave up on again after a pause, without polling meanwhile', () => {
    vi.useFakeTimers();
    const live = TestBed.inject(LiveEvents);
    live.connect('org-1');
    sources[0].readyState = 2;
    sources[0].emit('error');
    expect(sources).toHaveLength(1);
    vi.advanceTimersByTime(4_999);
    expect(sources).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(sources).toHaveLength(2);
    sources[1].readyState = 2;
    sources[1].emit('error');
    vi.advanceTimersByTime(9_999);
    expect(sources).toHaveLength(2);
    vi.advanceTimersByTime(1);
    expect(sources).toHaveLength(3);
  });

  it('leaves a stream the browser is reconnecting itself alone', () => {
    vi.useFakeTimers();
    const live = TestBed.inject(LiveEvents);
    live.connect('org-1');
    sources[0].readyState = 0;
    sources[0].emit('error');
    vi.advanceTimersByTime(60_000);
    expect(sources).toHaveLength(1);
    expect(sources[0].closed).toBe(false);
  });

  it('closes the stream when released, and when another organization connects', () => {
    const live = TestBed.inject(LiveEvents);
    const release = live.connect('org-1');
    live.connect('org-2');
    expect(sources[0].closed).toBe(true);
    release();
    expect(sources[1].closed).toBe(false);
    live.connect('org-3')();
    expect(sources[2].closed).toBe(true);
  });

  it('reloads the reads of a page on every change, not when the page opens', () => {
    const reload = vi.fn(() => true);
    @Component({ template: '' })
    class Page {
      constructor() {
        reloadOnLiveChange({ reload });
      }
    }
    const live = TestBed.inject(LiveEvents);
    live.connect('org-1');
    const fixture = TestBed.createComponent(Page);
    TestBed.tick();
    expect(reload).not.toHaveBeenCalled();
    sources[0].emit('changed');
    TestBed.tick();
    expect(reload).toHaveBeenCalledTimes(1);
    sources[0].emit('changed');
    TestBed.tick();
    expect(reload).toHaveBeenCalledTimes(2);
    fixture.destroy();
    sources[0].emit('changed');
    TestBed.tick();
    expect(reload).toHaveBeenCalledTimes(2);
  });
});
