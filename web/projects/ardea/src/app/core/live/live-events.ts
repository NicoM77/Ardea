import { effect, inject, Injectable, InjectionToken, signal, untracked } from '@angular/core';
import { API_BASE_URL } from '../http/api-base-url.interceptor';

/** The part of the browser's EventSource the live stream uses; replaced in tests. */
export interface LiveSource {
  readonly readyState: number;
  addEventListener(type: string, listener: () => void): void;
  close(): void;
}

export const LIVE_SOURCE = new InjectionToken<(url: string) => LiveSource>('LIVE_SOURCE', {
  providedIn: 'root',
  factory: () => (url: string) => new EventSource(url),
});

/** EventSource.CLOSED: the browser gave up and will not reconnect by itself. */
const CLOSED = 2;
const FIRST_RETRY_MS = 5_000;
const LAST_RETRY_MS = 300_000;

/**
 * The organization's live stream (master plan 11.7, task 3.11): one Server-Sent Events connection
 * to `GET /api/v1/orgs/{orgId}/events` while a page of the organization is open. The events carry
 * no data; each `changed` or `resync` (and a `ready` after a lost connection, since signals may
 * have been missed meanwhile) counts `changes` up, and the pages reload what they show through
 * the API. Nothing polls: the browser reconnects a dropped stream on its own, and a stream it gave
 * up on is opened again after a growing pause.
 */
@Injectable({ providedIn: 'root' })
export class LiveEvents {
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  private readonly open = inject(LIVE_SOURCE);

  private readonly count = signal(0);
  /** Counts up whenever what the organization's pages show may have changed. */
  readonly changes = this.count.asReadonly();

  private source: LiveSource | null = null;
  private organization: string | null = null;
  private retry: ReturnType<typeof setTimeout> | undefined;
  private delay = FIRST_RETRY_MS;
  private wasReady = false;

  /** Holds the stream of one organization until the returned function is called. */
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
      // Refused or broken for good (the browser does not retry): open it again later.
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

/** Anything that can be read again, such as an `httpResource`. */
export interface Reloadable {
  reload(): boolean;
}

/**
 * Reloads the given reads whenever the live stream says something changed. Call in an injection
 * context (a page's constructor or field initializers).
 */
export function reloadOnLiveChange(...reads: Reloadable[]): void {
  const live = inject(LiveEvents);
  const start = live.changes();
  // Created in the page's injection context, the effect ends with the page.
  effect(() => {
    if (live.changes() === start) return;
    untracked(() => reads.forEach((read) => read.reload()));
  });
}
