# ADR 045: Authenticator codes work once per account

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 013 (cookie sessions on Identity), ADR 023 (one transaction per unit of work)
- Master plan: 9.3, 9.7; issue #85

## Context

Sign-in accepted an authenticator (TOTP) code a second time. Identity's `AuthenticatorTokenProvider` checks only
that a code belongs to one of the time steps −2 … +2 around now and keeps no record of the steps it has seen. Anyone
who sees a code typed and knows the password could sign in with it within the same ±90 seconds. RFC 6238 §5.2
recommends, and OWASP ASVS 2.8.4 requires, that a time-based code is accepted only once. The second sign-in step
and turning two-factor on and off all go through that provider.

Recovery codes looked single-use but were not under concurrency. Identity removes a redeemed code from the stored
list and saves the list and the account in one `SaveChanges`, with the account guarded by its concurrency stamp.
Module contexts run with `AutoTransactionBehavior.Never` inside the unit of work (ADR 023), so when the stamp check
failed the list had already been written, and the failure path committed the unit of work anyway. A request that
had read the list before another one redeemed code 0 could write code 0 back while redeeming code 1.

Identity's `UpdateAsync` also marks every column of the account modified and writes all of them from the entity as
loaded. Any request that loaded the account before a code was claimed, and saved it afterwards (a lockout count
after a wrong password still being hashed, or the second sign-in of a concurrent pair), would write the older step
back and make the code valid again.

## Decision

1. **Own provider under Identity's name.** `SingleUseAuthenticatorTokenProvider` is registered as
   `TokenOptions.DefaultAuthenticatorProvider`, so `TwoFactorAuthenticatorSignInAsync` and
   `VerifyTwoFactorTokenAsync` use it without changes to the endpoints. It computes RFC 6238 itself
   (`AuthenticatorCode`: HMAC-SHA1, 30-second steps, six digits, framework crypto only) and returns the time step a
   code matched instead of a yes or no. The drift window stays −2 … +2 steps; the clock is `TimeProvider`.
2. **Last used step on the account.** `iam.user.totp_last_step` (bigint, null until the first code). A code is
   accepted only if its step is later than the stored one; then that step is stored. A code of the same or an
   earlier step, inside the window or not, is refused. The rule is per account, not per key, so after turning
   two-factor off and on again within the same 30 seconds the new key's first code waits for the next step.
3. **Compare-and-set in the unit of work.** The provider claims the step with
   `UPDATE iam."user" SET totp_last_step = @step WHERE id = @user AND (totp_last_step IS NULL OR totp_last_step < @step)`
   and accepts only if it changed one row. It runs in the request's transaction, together with the session it
   creates or the change it permits. A request that throws afterwards rolls the claim back. A request whose
   follow-up fails without throwing (Identity cannot reset the lockout count after a correct code) commits with
   the step claimed and no sign-in: it fails closed, and the user enters the next code.
4. **Only the compare-and-set writes the column.** EF ignores it on update
   (`PropertySaveBehavior.Ignore`), so no account saved by Identity writes a stale step back. As defence in depth a
   trigger (`iam.keep_totp_last_step`, not `SECURITY DEFINER`) keeps the larger of the old and the new value on
   every update: the step never goes back and never becomes empty again, whatever writes it.
5. **One code check per account at a time.** The second sign-in step and turning two-factor on and off lock the
   account row (`SELECT … FOR UPDATE`) as their first statement, before Identity loads the account
   (`AccountLookup.LockAsync`). Recovery codes are redeemed only in these flows. A second check for the same account
   waits for the first to commit, then loads the account, the recovery codes and the step as the first one left
   them. So of two requests with the same code exactly one succeeds and the other gets a normal «invalid code»;
   two requests with different recovery codes both succeed and both codes stay used. The compare-and-set stays the
   rule itself: it does not depend on the lock.
6. **A refused replay is a wrong code.** Same answer (`invalid_code`), same lockout count, same audit entry as any
   wrong code. The server logs a warning with the account id only (event 3002); never the code. A replay costs one
   `UPDATE` that changes nothing more than a wrong code does. That timing difference is accepted: it only tells
   someone who already has a valid code that it was used, which the answer itself already tells.
7. **No new system operation.** The account writes and locks its own row; the existing row-level policy on
   `iam.user` (ADR 029) covers the column and the lock.

## Alternatives

- **An `iam.user_token` entry per account** (as the issue suggested): a second row next to the encrypted key,
  written through the user store. The store encrypts every token and saves it through the change tracker with the
  next account update, which makes an atomic compare-and-set awkward; a column on the account row gets it from one
  `UPDATE`.
- **Rolling back the unit of work when Identity reports a concurrency failure**, instead of the lock: it would
  need a check after every Identity call in every flow, and the concurrent TOTP case would still depend on who
  loads first. The lock removes the race at its source.
- **Storing used codes** instead of the step: more rows, and an older code from the window would still be accepted.
- **Shrinking the window to the current step only**: does not stop a replay within the 30 seconds and breaks phones
  whose clock is a little off.

## Consequences

- A user who signs in on two devices within one step needs the next code on the second one; the end-to-end helper
  `Authenticator.nextCode()` already waits for a fresh step.
- Code checks for one account run one after the other. Each holds the lock only for its own short transaction,
  which calls nothing outside the database.
- Tests that turned two-factor on and signed in with the same code now use the next step's code.
- If the server clock ran ahead and then is corrected, codes are refused until real time passes the step stored
  while it ran ahead; a recovery code still works.
- Evidence: `AuthenticatorCodeTests` (RFC test vectors, window, malformed input), `AuthenticatorReplayTests`
  (replay after enabling, older code after a newer one, concurrent sign-ins with one code, an account loaded before
  the claim and saved after it, the trigger, lockout, turning two-factor off, concurrent recovery codes, the same
  and different ones), `SignInTests.An_authenticator_code_signs_in_only_once`.
