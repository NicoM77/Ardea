# ADR 049: Above the hourly cap, a message goes out with the next digest of its channel or an hour later

- Status: accepted
- Date: 2026-11-20
- Refines: ADR 038 (decisions 3 and 6, consequence «Rate limit»)
- Master plan: 8.4, 8.6
- Issue: #102

## Context

Master plan 8.6 caps immediate messages at 60 per channel, organisation and hour; above, the message «moves into the
digest». ADR 038 wrote such a message as `suppressed` and left the event to the digests whose targets matched it anyway.
A recipient without a digest target on that channel never heard of the event there. Because the notification state is
written when a message is decided (ADR 038 decision 3), it also recorded the event as reported, so a later message at
the same level stayed silent as well: the owner of the issue's example never got a level 5 action by e-mail.

## Decision

1. **The cap still decides at once, and the state stays written on decision.** A message above the cap is written as
   `suppressed` with the cap's reason (`Evaluator.CappedReason`), its events linked to it as before, so the log shows
   that the cap took effect. The state is recorded as before: the event now reaches the recipient on that channel by
   one of the two ways below, so the state says what the recipient will have heard.
2. **Overflow per recipient and channel, one hour later.** The first capped message of a recipient and channel queues
   `notify.send-overflow` for one hour later, coalesced per organisation, recipient and channel; further capped events
   in that hour join the same job. The job runs in the organisation's unit of work under the evaluation's advisory lock
   (so it never races an evaluation for the waiting message), takes the events of that recipient's capped messages on
   that channel, leaves out those a digest of the same recipient and channel already took, and moves the rest
   (`delivery_event.delivery_id`) into the recipient's waiting immediate message on that channel, or into a new one
   sent at once. That message goes through `notify.deliver` like any other: three steps, retries, and the owners'
   warning when level 5 is given up.
3. **Never twice with a digest target.** Whichever comes first carries the event in full. A digest of the recipient and
   channel that came first (a digest delivery that is not `dead` or `suppressed`, of a target that noted the event)
   makes the overflow leave the event out. An overflow that came first is an immediate message like any other, so the
   digest marks the event «already reported» (master plan 8.4).
4. **Bounded.** At most one overflow per recipient, channel and hour comes on top of the 60, so a flood of events
   cannot become a flood of messages. An overflow message counts towards the cap of the hour it is sent in.
5. **No schema change.** The capped messages are found by their reason, which only the cap writes; after the overflow
   they keep their row and reason but no events.

Rejected:

- *Holding the message as `pending` until the hour has room.* Every held message of the organisation would go out
  the moment one slot frees, and the next capped ones a few seconds later: bursts instead of a cap. The log would no
  longer show that the cap took effect.
- *An implicit digest target per recipient.* A schema change and a second digest path for the same result.
- *Writing the state only once a message went out.* Contradicts ADR 038 decision 3: an event during the two minutes
  of bundling would send the same again.

## Consequences

- A capped event reaches the recipient on its channel at most one hour late; sooner when their digest on that channel
  comes first.
- The delivery log keeps the suppressed row with the cap's reason; its events show under the message that carried them.
- A recipient who left or unsubscribed before the overflow gets nothing, like any message (the overflow is
  suppressed when it is sent).
