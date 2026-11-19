import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { NotificationsService, type PushPublicKeyView } from '../../core/api/client';

/**
 * What this browser can do about push (master plan 11.9): `unsupported` (no push at all), `install` (iPhone or
 * iPad in a Safari tab: push needs the app on the Home Screen first), `blocked` (the person denied it),
 * `off`, `on`.
 */
export type PushState = 'checking' | 'unsupported' | 'install' | 'blocked' | 'off' | 'on';

/** The browser's subscription key comes base64url; PushManager wants the bytes. */
export function keyBytes(base64url: string): Uint8Array<ArrayBuffer> {
  const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');
  const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
  const raw = atob(padded);
  const bytes = new Uint8Array(new ArrayBuffer(raw.length));
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  return bytes;
}

/** iPhone and iPad, also an iPad that reports itself as a Mac. */
export function isAppleMobile(userAgent: string, maxTouchPoints: number): boolean {
  return /iPhone|iPad|iPod/.test(userAgent) || (/Macintosh/.test(userAgent) && maxTouchPoints > 1);
}

/**
 * Web Push on this device (task 5.5, ADR 040): registers the service worker, asks for permission on a tap,
 * subscribes with the instance's key and hands the subscription to the API; turning it off removes both.
 */
@Injectable({ providedIn: 'root' })
export class PushDevice {
  private readonly api = inject(NotificationsService);
  private readonly http = inject(HttpClient);
  private readonly window = inject(DOCUMENT).defaultView;

  private readonly current = signal<PushState>('checking');
  readonly state = this.current.asReadonly();
  /** The endpoint of this browser's subscription, to find it among the devices. */
  readonly endpoint = signal<string | null>(null);

  /** Looks at the browser and its subscription without asking anything. */
  async check(): Promise<PushState> {
    const w = this.window;
    if (!w || !w.navigator.serviceWorker) return this.set('unsupported');
    const standalone =
      w.matchMedia?.('(display-mode: standalone)').matches ||
      (w.navigator as Navigator & { standalone?: boolean }).standalone === true;
    if (!('PushManager' in w) || typeof w.Notification === 'undefined') {
      return this.set(
        isAppleMobile(w.navigator.userAgent, w.navigator.maxTouchPoints) && !standalone
          ? 'install'
          : 'unsupported',
      );
    }
    if (w.Notification.permission === 'denied') return this.set('blocked');
    const registration = await w.navigator.serviceWorker.getRegistration('/');
    const subscription = await registration?.pushManager.getSubscription();
    this.endpoint.set(subscription?.endpoint ?? null);
    return this.set(subscription && w.Notification.permission === 'granted' ? 'on' : 'off');
  }

  /** Turns push on: called from a tap, because browsers ask for permission only then. */
  async enable(): Promise<PushState> {
    const w = this.window;
    if (!w) return this.set('unsupported');
    const permission = await w.Notification.requestPermission();
    if (permission !== 'granted') return this.set(permission === 'denied' ? 'blocked' : 'off');
    const registration = await w.navigator.serviceWorker.register('/sw.js', { scope: '/' });
    await w.navigator.serviceWorker.ready;
    // A read: the generated client offers it as a resource only.
    const { publicKey } = await firstValueFrom(
      this.http.get<PushPublicKeyView>('/api/v1/push/public-key'),
    );
    const key = keyBytes(publicKey);
    let subscription = await registration.pushManager.getSubscription();
    // A subscription made with another key (the instance's pair changed) cannot be used any more.
    const existing = subscription?.options.applicationServerKey;
    if (subscription && existing && !sameKey(new Uint8Array(existing), key)) {
      await subscription.unsubscribe();
      subscription = null;
    }
    subscription ??= await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: key,
    });
    const json = subscription.toJSON();
    await firstValueFrom(
      this.api.createPushSubscription({
        endpoint: subscription.endpoint,
        keys: { p256dh: json.keys?.['p256dh'] ?? null, auth: json.keys?.['auth'] ?? null },
        expirationTime: subscription.expirationTime,
      }),
    );
    this.endpoint.set(subscription.endpoint);
    return this.set('on');
  }

  /** Turns push off on this device: the server forgets it, then the browser does. */
  async disable(): Promise<PushState> {
    const registration = await this.window?.navigator.serviceWorker.getRegistration('/');
    const subscription = await registration?.pushManager.getSubscription();
    if (subscription) {
      await firstValueFrom(
        this.api.deletePushSubscriptionByEndpoint({ endpoint: subscription.endpoint }),
      );
      await subscription.unsubscribe();
    }
    this.endpoint.set(null);
    return this.set('off');
  }

  private set(state: PushState): PushState {
    this.current.set(state);
    return state;
  }
}

function sameKey(a: Uint8Array, b: Uint8Array): boolean {
  return a.length === b.length && a.every((value, i) => value === b[i]);
}
