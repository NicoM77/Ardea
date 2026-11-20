import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { NotificationsService, type PushPublicKeyView } from '../../core/api/client';

export type PushState = 'checking' | 'unsupported' | 'install' | 'blocked' | 'off' | 'on';

export function keyBytes(base64url: string): Uint8Array<ArrayBuffer> {
  const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');
  const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
  const raw = atob(padded);
  const bytes = new Uint8Array(new ArrayBuffer(raw.length));
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  return bytes;
}

export function isAppleMobile(userAgent: string, maxTouchPoints: number): boolean {
  return /iPhone|iPad|iPod/.test(userAgent) || (/Macintosh/.test(userAgent) && maxTouchPoints > 1);
}

@Injectable({ providedIn: 'root' })
export class PushDevice {
  private readonly api = inject(NotificationsService);
  private readonly http = inject(HttpClient);
  private readonly window = inject(DOCUMENT).defaultView;

  private readonly current = signal<PushState>('checking');
  readonly state = this.current.asReadonly();
  readonly endpoint = signal<string | null>(null);

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
    if (!subscription || w.Notification.permission !== 'granted') {
      this.endpoint.set(null);
      return this.set('off');
    }
    try {
      await this.register(subscription);
      return this.set('on');
    } catch {
      this.endpoint.set(null);
      return this.set('off');
    }
  }

  async forget(): Promise<void> {
    const w = this.window;
    if (!w?.navigator.serviceWorker || !('PushManager' in w)) return;
    const work = (async () => {
      const registration = await w.navigator.serviceWorker.getRegistration('/');
      const subscription = await registration?.pushManager.getSubscription();
      if (!subscription) return;
      await firstValueFrom(
        this.api.deletePushSubscriptionByEndpoint({ endpoint: subscription.endpoint }),
      ).catch(() => undefined);
      await subscription.unsubscribe().catch(() => false);
    })();
    await Promise.race([
      work.catch(() => undefined),
      new Promise((done) => setTimeout(done, 3000)),
    ]);
    this.endpoint.set(null);
    this.current.set('off');
  }

  async enable(): Promise<PushState> {
    const w = this.window;
    if (!w) return this.set('unsupported');
    const permission = await w.Notification.requestPermission();
    if (permission !== 'granted') return this.set(permission === 'denied' ? 'blocked' : 'off');
    const registration = await w.navigator.serviceWorker.register('/sw.js', { scope: '/' });
    await w.navigator.serviceWorker.ready;
    const { publicKey } = await firstValueFrom(
      this.http.get<PushPublicKeyView>('/api/v1/push/public-key'),
    );
    const key = keyBytes(publicKey);
    let subscription = await registration.pushManager.getSubscription();
    const existing = subscription?.options.applicationServerKey;
    if (subscription && existing && !sameKey(new Uint8Array(existing), key)) {
      await subscription.unsubscribe();
      subscription = null;
    }
    subscription ??= await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: key,
    });
    await this.register(subscription);
    return this.set('on');
  }

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

  private async register(subscription: PushSubscription): Promise<void> {
    const json = subscription.toJSON();
    await firstValueFrom(
      this.api.createPushSubscription({
        endpoint: subscription.endpoint,
        keys: { p256dh: json.keys?.['p256dh'] ?? null, auth: json.keys?.['auth'] ?? null },
        expirationTime: subscription.expirationTime,
      }),
    );
    this.endpoint.set(subscription.endpoint);
  }

  private set(state: PushState): PushState {
    this.current.set(state);
    return state;
  }
}

function sameKey(a: Uint8Array, b: Uint8Array): boolean {
  return a.length === b.length && a.every((value, i) => value === b[i]);
}
