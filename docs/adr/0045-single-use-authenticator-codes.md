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
and turning two-factor on and off all go through that provider. Recovery codes were already single-use: Identity
removes a redeemed code and saves the account under its concurrency stamp, so of two concurrent redemptions one
fails.

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
   creates or the change it permits; a request that fails later rolls the claim back. Of two requests with the
   same code, the second waits for the first one's row lock and, in `READ COMMITTED`, re-checks the condition
   against the committed row, finds the step taken and is refused: exactly one succeeds, the other gets a normal
   «invalid code», not an error.
4. **A refused replay is a wrong code.** Same answer (`invalid_code`), same lockout count, same audit entry as any
   wrong code, so the answer tells nothing. The server logs a warning with the account id only (event 3002); never
   the code.
5. **No new system operation.** The account writes its own row; the existing row-level policy on `iam.user`
   (ADR 029) covers the column.

## Alternatives

- **An `iam.user_token` entry per account** (as the issue suggested): a second row next to the encrypted key,
  written through the user store. The store encrypts every token and saves it through the change tracker with the
  next account update, which makes an atomic compare-and-set awkward; a column on the account row gets it from one
  `UPDATE`.
- **Storing used codes** instead of the step: more rows, and an older code from the window would still be accepted.
- **Shrinking the window to the current step only**: does not stop a replay within the 30 seconds and breaks phones
  whose clock is a little off.

## Consequences

- A user who signs in on two devices within one step needs the next code on the second one; the end-to-end helper
  `Authenticator.nextCode()` already waits for a fresh step.
- Tests that turned two-factor on and signed in with the same code now use the next step's code.
- A drift of the server clock forward by more than the window would lock codes out until real time catches up;
  this is no different from today, where such a drift already refuses every code.
- Evidence: `AuthenticatorCodeTests` (RFC test vectors, window, malformed input), `AuthenticatorReplayTests`
  (replay after enabling, older code after a newer one, concurrent sign-ins, lockout, turning two-factor off,
  concurrent recovery codes), `SignInTests.An_authenticator_code_signs_in_only_once`.
