# Phase 5 acceptance: notifications

Master plan chapter 14, phase 5: the criteria of chapter 8.8. Criteria are checked by fast tests in the pipeline
or observed once on a real instance. This record is written from the tests of `main` at the merge of PR #72
(tasks 5.1 to 5.8, PRs #66 and #72); nothing was observed on an instance. The tests of `Ardea.Notifications.Tests`
run against a real PostgreSQL with a fake clock, a capturing e-mail sender, a local listener for webhooks and a fake
push service; the SMTP test uses a Mailpit container.

## Criteria

| Criterion | Evidence | Result |
|---|---|---|
| The same event processed three times gives exactly one delivery per recipient and channel | `NotificationStateTests.Same_event_processed_three_times_yields_one_delivery_per_recipient_and_channel` (in-app and e-mail; the delivery job running twice sends once) | Met |
| Two rules with the same recipient and channel give one message for an event | `NotificationStateTests.Two_rules_with_the_same_recipient_and_channel_yield_one_message_for_an_event` | Met |
| A level that rises on an action already reported notifies again; more findings or a changed description do not | `NotificationStateTests.A_raised_level_notifies_again_but_more_findings_or_a_new_description_do_not` (a changed description changes nothing in Assessment, so the test sends no event for it) | Met |
| Rise, fall and rise again give two messages about the rise, also for a recipient who did not get the all-clear in between | `NotificationStateTests.Raise_lower_and_raise_again_yield_two_raise_messages_also_for_a_recipient_without_the_all_clear` | Met |
| Three events for the same recipient within two minutes give one message | `NotificationStateTests.Three_events_for_the_same_recipient_within_two_minutes_yield_one_message` (events 40 seconds apart; one mail with three items; the next event after sending starts a new one) | Met |
| A new project with 40 hits in the look-back gives exactly one message | `NotificationStateTests.A_new_project_with_forty_hits_in_the_look_back_yields_exactly_one_message` (one `backfill.completed`, one notification, one mail); that a first fill writes no `action.raised` and exactly one `backfill.completed` is `ActionTests.A_new_project_with_40_hits_gets_backfill_findings_and_exactly_one_summary` | Met |
| A warning without identifier appears at every organisation once in-app and once in the next digest | `DigestAndWarningTests.A_warning_without_identifier_appears_in_every_organisation_once_in_app_and_once_in_the_next_digest` (two organisations, the distribution run three times; the weekly digest has it once and the one after does not repeat it) | Met |
| Digest: comes at the set time in the organisation's time zone, holds everything since the last one, and comes empty when the rule says so | `DigestAndWarningTests.Digest_comes_at_the_set_time_in_the_organisations_time_zone_holds_everything_since_the_last_and_comes_empty_when_the_rule_says_so` (07:30 in New York; a next day with only the new item; an empty digest only for the rule that asks for it), `RobustnessTests.A_new_time_zone_moves_the_next_digest_to_the_same_local_time` | Met |
| E-mail: an unreachable SMTP server delays the delivery but does not lose it | `EmailChannelTests.An_unreachable_smtp_server_delays_the_delivery_but_does_not_lose_it` (first attempt fails and the job stays pending; the retry after the back-off arrives at Mailpit with its `List-Unsubscribe` header) | Met |
| Web Push: creating and removing a subscription works in the automatic test in Chromium | `web/e2e/flows.spec.ts` «push is turned on and off on this device in Chromium» (Playwright: the page subscribes and posts the subscription, turns off by endpoint, and sign-out removes it first; axe check). The browser's `PushManager` and the API are stubbed there, because no push service is reachable in a test. Server side: `WebPushTests.Subscriptions_are_taken_only_for_the_browser_makers_push_services_and_only_their_owner_reaches_them`, `WebPushTests.The_same_device_subscribing_twice_at_once_ends_with_one_subscription` | Met, with a stubbed browser push manager |
| Web Push: an expired subscription disappears after the first failed attempt | `WebPushTests.The_push_service_receives_an_encrypted_message_signed_with_the_instance_key_and_a_gone_subscription_disappears` (a 410 from the fake push service removes the subscription at once; the delivery is dead, no retry), `WebPushTests.Devices_are_reached_together_within_a_budget_and_a_gone_one_is_removed_although_the_message_is_retried` | Met |
| Web Push: the message is encrypted and signed correctly | `WebPushTests.Encryption_matches_the_example_of_RFC_8291` and the first test above (the fake service's request decrypts with the device's keys; the ES256 token verifies with the instance's public key) | Met, against a fake push service |
| Web Push on iOS, checked by hand on an installed web app | Needs a person with an iPhone or iPad and the installed web app; no test runs on iOS | **Open**: not run; for the owner |
| Web Push through a real push service (Google, Mozilla, Apple) | Needs a real push service; no test contacts one (PR #72: no real push service contacted) | **Open**: not run; for the owner |
| Webhook: a receiver can verify the authenticity of every message with the shared secret | `WebhookChannelTests.A_receiver_verifies_every_webhook_with_the_shared_secret` (HMAC-SHA256 over «{t}.{body}» as a receiver computes it; a changed body fails; the secret is shown once and stored encrypted; 503 is retried, 410 is final) | Met |
| Webhook: a target in the private network is refused without release | `WebhookChannelTests.A_webhook_target_in_the_private_network_is_refused_without_release` (private ranges, loopback, link-local and `::1` refused; one released host is accepted while loopback and metadata addresses stay closed), `WebPushTests.A_push_endpoint_that_resolves_to_an_internal_address_is_refused_and_removed` | Met |

## Open points for the owner

- **iOS check by hand.** Install Ardea to the Home Screen of an iPhone or iPad, turn on push on the settings page
  *Your notifications* and send the device's test message from its row; check that the guide leads through «Add to
  Home Screen» before permission is asked. Write the result and the iOS version here.
- **A real push service.** One real subscription each in Chrome (Google), Firefox (Mozilla) and, with the iOS check,
  Safari (Apple): a level 5 message arrives with high urgency, and a removed browser subscription disappears after
  the first refusal. Until then the channel is verified against RFC 8291 and a fake service only.
- **Review the notification mockups** (E15) against the built screens: [`docs/design/mockups/notification-rules.html`](https://github.com/NicoM77/Ardea/tree/archive/planning-and-design/docs/design/mockups/notification-rules.html),
  `notifications.html`, `notification-settings.html` and `notification-link.html`. Deviations are listed in PR #72:
  the delivery log has no chart, «Send a test» is on each device row, webhook rows show «New secret» and «Delete» as
  buttons, and underline tabs stay flush on their rule.
- **Not part of the criteria, but unchecked:**
  - The escalation of master plan 8.7 is not in the MVP (PR #66).
  - `repository.unavailable` can be chosen in a rule, but nothing raises it yet (PR #66, Assets 16.6).
  - Over 60 immediate messages per channel and hour, further ones are `suppressed` and appear only in digests whose
    targets match them (PR #66); the owner may want another limit or a hint on the page.
  - The criteria are checked with a fake clock and fake services. How digests, retries and push behave over days on a
    real instance is not observed; the acceptance of all phases on the release images (task 7.11) is the place for it.
