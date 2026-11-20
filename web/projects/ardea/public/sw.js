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
    if (url.origin === self.location.origin) target = url;
  } catch {
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
    }
  }
  await self.clients.openWindow(href);
}
