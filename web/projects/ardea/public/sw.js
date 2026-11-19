/*
 * Ardea's service worker: Web Push only (master plan 8.5, 11.9; task 5.5, ADR 040). It has no fetch
 * handler and caches nothing; the app works as before without it.
 *
 * A push message is JSON in the form of Declarative Web Push:
 * { "web_push": 8030, "notification": { "title", "body", "navigate", "lang", "tag" }, "ardea": { ... } }.
 * Safari shows such a message by itself; everywhere else this worker shows it, and a click opens
 * `navigate`, a page of this instance.
 */
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

self.addEventListener('push', (event) => {
  let message = {};
  try {
    message = event.data ? event.data.json() : {};
  } catch {
    message = { notification: { title: 'Ardea', body: event.data ? event.data.text() : '' } };
  }
  const notification = message.notification || {};
  event.waitUntil(
    self.registration.showNotification(notification.title || 'Ardea', {
      body: notification.body || '',
      lang: notification.lang || undefined,
      tag: notification.tag || undefined,
      icon: '/brand/icon-192.png',
      data: { url: notification.navigate || '/' },
      // Critical stays on screen until it is dealt with.
      requireInteraction: !!(message.ardea && message.ardea.level === 5),
    }),
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  let target = new URL('/', self.location.origin);
  try {
    const url = new URL(
      (event.notification.data && event.notification.data.url) || '/',
      self.location.origin,
    );
    // Only pages of this instance: a message never sends the browser elsewhere.
    if (url.origin === self.location.origin) target = url;
  } catch {
    // Keep the start page.
  }
  event.waitUntil(open(target.href));
});

async function open(href) {
  const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
  for (const client of windows) {
    if (new URL(client.url).origin !== self.location.origin) continue;
    try {
      const focused = await client.focus();
      if ('navigate' in focused) {
        await focused.navigate(href);
        return;
      }
    } catch {
      // A window this worker does not control cannot be navigated; open a new one.
    }
  }
  await self.clients.openWindow(href);
}
