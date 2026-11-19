# ADR 041: Web Push

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 038 (notifications), ADR 029 (system functions and policies)
- Master plan: 4.7, 8.5, 8.8, 9.2, 9.4, 9.5, 10.2, 11.9, task 5.5

## Context

Master plan 8.5 asks for standard Web Push with the instance's own VAPID key pair: content encrypted end to end,
urgency `high` only for level 5, expired subscriptions (404, 410) removed, endpoints only at the browser makers' push
services. 11.9 asks for a message that both a service worker and Safari's Declarative Web Push understand, and for iOS
to be led through «Add to Home Screen» before permission is asked. 4.7 lists `notify.push_subscription` as user data;
9.2 lets the worker read push subscriptions for a named recipient inside its job's tenant. ADR 038 left the channel,
the table and the push target of default rule 1 to this task. Open were where the key pair lives, whether a device
belongs to a user or to a membership, which library encrypts, and what counts as delivered when a member has several
devices.

## Decision

1. **Key pair.** A P-256 pair per instance. If `ARDEA_VAPID__PUBLIC_KEY` and `ARDEA_VAPID__PRIVATE_KEY` are set
   (base64url, 65-byte uncompressed point and 32-byte scalar, checked at start to be one pair), they are used;
   otherwise the first process generates a pair and stores both halves as one secret in `platform.setting`
   (`notify.vapid_keys`, purpose `notify.vapid-keys`, encrypted with `ISecretProtector`). It inserts only if absent and
   reads back, so two processes starting together end with the same pair. The pair is cached per process.
   `ARDEA_VAPID__SUBJECT` (`mailto:` or `https:`) names the contact; the public address by default. Changing the pair
   invalidates every subscription; the push services answer 401 or 403 and the subscriptions go (decision 6).
2. **Subscriptions belong to the user.** `notify.push_subscription` (user id, endpoint, its SHA-256 for the unique
   index per user, `p256dh`, `auth`, user agent and a label such as «Chrome on macOS», `failed_count`,
   `last_success_at`), at most 20 per user, the oldest dropped. A device receives the push messages of every
   organisation its user belongs to; each organisation's mute in `user_preference` applies. Policies: `own` (the user
   reads and writes their rows) and, for a unit of work of an organisation without a user, `deliver_read`,
   `deliver_update` and `deliver_delete` on the rows of members of that organisation. A browser that subscribes for
   a second user on the same device gets a second row; both users are reached there.
3. **Endpoints.** `GET /push/public-key`; `GET, POST /me/push-subscriptions` (POST takes the browser's
   `PushSubscription.toJSON()`; 201 for a new device, 200 for one already known); `DELETE /me/push-subscriptions?endpoint=`
   (this device turns push off; idempotent); `DELETE /me/push-subscriptions/{id}`; `POST /me/push-subscriptions/{id}/test`
   (sends a test message now, holding no transaction while waiting). An endpoint is accepted only over https at
   `fcm.googleapis.com`, `updates.push.services.mozilla.com`, `web.push.apple.com`, `*.push.apple.com`,
   `*.notify.windows.com`, or a host in `ARDEA_PUSH_SERVICE_HOSTS` (a self-hosted push service). No DNS lookup at
   subscription time; the outbound guard decides when sending.
4. **Sending.** `WebPushChannel` is one more `INotificationChannel`. The three steps of ADR 038 stay: the first step
   reads the member's devices through the worker policy, the requests go out without a transaction, the outcome is
   written in the job's unit of work (or, before a retry, in the unit of work that records the failure). Every request
   goes through `OutboundAddresses.CreateHandler` with internal addresses never allowed, follows no redirect and is not
   logged, because an endpoint is a capability. Headers: `Content-Encoding: aes128gcm`, `TTL: 86400`, `Urgency: high`
   for level 5 else `normal`, `Topic` from the delivery id (a retry replaces a message the service still holds),
   `Authorization: vapid t=<ES256 JWT>, k=<public key>` with `aud` the endpoint's origin, `exp` 12 hours ahead and
   `sub` the subject. Bundling (two minutes), the hourly limit and the level 5 fallback of ADR 038 apply as to e-mail.
5. **Payload.** JSON in the form of Declarative Web Push:
   `{"web_push":8030,"notification":{"title","body","navigate","lang","tag"},"ardea":{"delivery","level","organization"}}`.
   One action: its in-app title and headline, `navigate` the action's page. Several: «[5 Critical] 3 updates in Acme»,
   the three highest titles, `navigate` the inbox `/o/{slug}/notifications`. The service worker shows the same
   notification and opens `navigate` on a click.
6. **Outcome per device, then per delivery.** 2xx: sent, `failed_count` 0, `last_success_at` now. 404 and 410, and a
   target the guard refuses: the subscription is removed at once. 401 and 403 (the device subscribed with another key)
   count, and remove it on the third in a row. 408, 429, 5xx and network errors are passing; other 4xx are final for
   this message. The delivery is `sent` if any device accepted it, retried if none did and any failure was passing,
   and `dead` otherwise. A member without a device is not a push recipient at all: the evaluation leaves them out, so
   no delivery and no log row is written.
7. **Default rule 1** («Critical at once») gets the push target for owners and admins, for new organisations in
   `DefaultRules` and for existing ones in the migration `WebPush`. Push targets are immediate only; a rule refuses a
   push digest.
8. **Encryption in our code.** RFC 8291 and RFC 8292 are implemented with `System.Security.Cryptography`
   (`ECDiffieHellman`, `HKDF`, `AesGcm`, `ECDsa`), about 150 lines, with a test against the example of RFC 8291
   Appendix A. The .NET packages looked at (NuGet, 19 Nov 2026): *WebPush* 1.0.13 (web-push-csharp, MPL-2.0) still
   depends on Portable.BouncyCastle, which is no longer maintained, for every target including net10.0;
   *Lib.Net.Http.WebPush* 3.3.1 (MIT, last release March 2025, newest target net6.0) is the work of one maintainer and
   comes with its own abstractions for keys and HTTP that would have to be wrapped again to sit behind the outbound
   guard. The primitives are in the framework; a package would add a dependency for the smallest part of the work.

## Alternatives

- **Subscriptions per membership.** Push would be turned on per organisation and device, the policy would be the
  tenant's. Rejected: the browser has one subscription per origin, so «off in one organisation» would have to keep it
  for the others anyway; the per-organisation mute already does that.
- **A push digest.** A summary on the lock screen at 08:00 adds little to the e-mail digest and the inbox.
- **Key pair from configuration only.** Every operator would have to generate one; a generated pair kept encrypted
  like the other secrets needs nothing.

## Consequences

- The web app registers a service worker (`/sw.js`) and a manifest; the personal settings turn push on per device,
  list the devices, send a test, and guide iOS users to the Home Screen first. iOS is checked by hand (master plan 8.8).
- `ARDEA_VAPID__PUBLIC_KEY`, `ARDEA_VAPID__PRIVATE_KEY`, `ARDEA_VAPID__SUBJECT` and `ARDEA_PUSH_SERVICE_HOSTS` are new.
- An instance that reaches the internet only through a proxy cannot send push, as for webhooks (ADR 038 decision 13).
- Losing the master key loses the stored pair: a new one is generated on the next start only after the old secret
  is removed, and every device subscribes again.
