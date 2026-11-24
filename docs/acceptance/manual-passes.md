# The two manual passes before a release

[`CONTRIBUTING.md`](../../CONTRIBUTING.md) keeps two short passes by hand before a release: the core flows with
keyboard and screen reader (roadmap task 6.3), and Web Push on an installed web app on iOS. This page is the script for
both, so one person can do them in about an hour and record the result. Everything else is checked by the pipeline
(axe on every page of the Playwright flows, the keyboard flows in `web/e2e/flows.spec.ts`).

## Before you start

- An instance of the release under test with the demo data: [Try it](../operations/try-it.md) (`seed-demo`), reached
  over HTTPS for the push pass (Caddy with `ARDEA_DOMAIN`, see [Installation](../operations/installation.md)).
- The demo owner's sign-in printed by `seed-demo`. Two-factor stays off for the demo owner; the sign-in with TOTP is
  checked once in flow 1.
- **Screen readers:** NVDA with Firefox or Chrome on Windows, and VoiceOver with Safari on macOS. One of the two is
  enough for a release; note which one you used.
- Keyboard only: put the mouse away. Use Tab, Shift+Tab, Enter, Space, the arrow keys and Esc.

What every step expects, in addition to what the step says:

- The focus is always visible (a 2 px ring) and never lands on something invisible or behind an open dialog.
- Every control is announced with its role and name («Mark as remediated, button»), every page with its heading.
- A change is confirmed by a toast that the screen reader announces, with Undo reachable by keyboard.
- Nothing needs a pointer, and nothing traps the focus except an open dialog or menu, which Esc closes.

## Pass 1: the core flows with keyboard and screen reader

| # | Flow | Steps | Expected |
|---|---|---|---|
| 1 | Sign in | Open the instance, sign in with e-mail and password. Once, on a user with two-factor on, enter the six-digit code. | The skip link is the first stop and jumps to the content. Errors are announced and named («The e-mail address or the password is not right»). The code field announces its purpose. After sign-in the overview's heading is announced. |
| 2 | Overview | Tab through the overview. | The sentence under the heading is read. Each reminder row reads priority word, title and project. The priority ring's numbers are readable as text («Critical 1, High 3…»), not only as a chart. |
| 3 | Notices | Open *Notices*. Answer one notice with **1** («Affects us»), one with **2** («Doesn't affect us»), then Undo the second from the toast. | Keys 1 and 2 act on the focused notice; the focus moves to the next one. The toast is announced; Undo is reachable with Tab and restores the notice. The *New* / *Answered* switch is announced as a selected option. |
| 4 | Reminder | Open a reminder from the overview. Use «Mark as remediated», then Undo. Then «Other outcome» with a reason, and back. | The three answers («What happened», «Are we affected?», «What to do») are read in order. The action bar's buttons are reachable on a phone-width window too (resize to 390 px). «Other outcome» opens a form whose fields have labels; Esc or Cancel returns the focus to the button. |
| 5 | ⋯ menus | On a project page, open the ⋯ menu («More actions for …»), move with the arrow keys, close with Esc. Choose «Archive», then Undo. | The trigger announces «More actions for <project>». The menu items are announced as menu items; Esc returns the focus to the trigger. The archive toast offers Undo. |
| 6 | Search | Press `/`, type a CVE key (for example one shown on the overview), press Enter. | The search dialog is announced as a dialog with its label; the results are announced as you type; Enter opens the vulnerability; Esc closes the dialog and returns the focus. |
| 7 | Stack | Open *Stack*, add a product with the «Add a product» combobox (type, arrow down, Enter), set its version, remove it, Undo. | The combobox announces its options and the chosen one. The list announces the new entry. Removing and Undo are announced. |
| 8 | Notification settings | Open *Your notifications* from the account menu. Turn e-mail off and on, change a reminder level's rhythm. | Switches announce on/off; the toast after each change is announced; no separate save button is needed. |
| 9 | Phone width | Repeat flows 3 and 4 in a 390 px wide window (or on a phone with the screen reader on). | The menu button opens the navigation as a dialog; Esc closes it. The action bar stays at the bottom of the screen. |

Record a finding with the flow number, what you did, what was announced or where the focus went, and what you
expected. A finding that blocks a flow for a keyboard or screen-reader user blocks the release.

## Pass 2: Web Push on an installed web app on iOS

Needs an iPhone or iPad with iOS 16.4 or later and the instance reachable over HTTPS with a certificate the device
trusts (a public domain with Caddy is simplest). The instance must reach `web.push.apple.com` and `*.push.apple.com`
([Operation](../operations/operations.md)).

| # | Step | Expected |
|---|---|---|
| 1 | Open the instance in Safari, sign in, open *Your notifications*. | The push card says to add Ardea to the Home Screen first, with the three steps. |
| 2 | Share → «Add to Home Screen», open Ardea from the Home Screen, sign in. | Ardea opens as an app without Safari's toolbar. |
| 3 | *Your notifications* → «Turn on push», allow when iOS asks. | The card shows «On for this device»; the device appears in the list. |
| 4 | «Send test» for this device. | «The push service took the test…» shows; a notification with Ardea's title arrives within a minute, also with the app closed. |
| 5 | Trigger a real notification: in a second browser, with the demo owner, use «Mark as remediated» on a reminder and Undo it, or let a rule match (*Notification rules*). | The notification arrives on the device; tapping it opens Ardea on the reminder. |
| 6 | «Turn off on this device». | No further notifications arrive; the device leaves the list. |

## Recording the result

Add a short section to the acceptance record of the release (for 1.0: [release-1.0.md](release-1.0.md), «The two
manual passes of chapter 13.6»): date, release, who, browser and screen reader or device, and per flow «passed» or
the finding. Findings go into issues; a blocking finding waits for its fix and a new release.
