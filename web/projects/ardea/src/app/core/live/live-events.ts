import { effect, inject, Injectable, InjectionToken, signal, untracked } from '@angular/core';
import { API_BASE_URL } from '../http/api-base-url.interceptor';

export interface LiveSource {
  readonly readyState: number;
  addEventListener(type: string, listener: () => void): void;
  close(): void;
}

export const LIVE_SOURCE = new InjectionToken<(url: string) => LiveSource>('LIVE_SOURCE', {
  providedIn: 'root',
  factory: () => (url: string) => new EventSource(url),
});

const CLOSED = 2;
const FIRST_RETRY_MS = 5_000;
const LAST_RETRY_MS = 300_000;

@Injectable({ providedIn: 'root' })
export class LiveEvents {
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  private readonly open = inject(LIVE_SOURCE);

  private readonly count = signal(0);
  readonly changes = this.count.asReadonly();

  private source: LiveSource | null = null;
  private organization: string | null = null;
  private retry: ReturnType<typeof setTimeout> | undefined;
  private delay = FIRST_RETRY_MS;
  private wasReady = false;

  connect(organizationId: string): () => void {
    this.disconnect();
    this.organization = organizationId;
    this.wasReady = false;
    this.delay = FIRST_RETRY_MS;
    this.start();
    return () => {
      if (this.organization === organizationId) this.disconnect();
    };
  }

  private start(): void {
    const id = this.organization;
    if (!id) return;
    const source = this.open(`${this.base}/api/v1/orgs/${encodeURIComponent(id)}/events`);
    this.source = source;
    source.addEventListener('ready', () => {
      this.delay = FIRST_RETRY_MS;
      if (this.wasReady) this.bump();
      this.wasReady = true;
    });
    source.addEventListener('changed', () => this.bump());
    source.addEventListener('resync', () => this.bump());
    source.addEventListener('error', () => {
      if (this.source !== source || source.readyState !== CLOSED) return;
      source.close();
      this.source = null;
      clearTimeout(this.retry);
      this.retry = setTimeout(() => this.start(), this.delay);
      this.delay = Math.min(this.delay * 2, LAST_RETRY_MS);
    });
  }

  private bump(): void {
    this.count.update((n) => n + 1);
  }

  private disconnect(): void {
    clearTimeout(this.retry);
    this.source?.close();
    this.source = null;
    this.organization = null;
  }
}

export interface Reloadable {
  reload(): boolean;
}

export function reloadOnLiveChange(...reads: Reloadable[]): void {
  const live = inject(LiveEvents);
  const start = live.changes();
  effect(() => {
    if (live.changes() === start) return;
    untracked(() => reads.forEach((read) => read.reload()));
  });
}
