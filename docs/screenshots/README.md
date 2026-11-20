# Screenshots

The images of the README and the website. They are generated, never edited by hand: `build/screenshots.sh`
starts an instance from the image, loads the demo data of [`ardea seed-demo`](../operations/try-it.md), signs in
as the demo owner and takes them with Playwright (`tests/e2e/screenshots/`). Run it again after a change to the
interface and commit the new images.

```bash
build/screenshots.sh              # builds the image from this checkout
build/screenshots.sh ardea:dev    # or takes an image you built
```

It needs Docker only and takes a few minutes. The instance is local, under its own Compose project, and is removed
with its volumes at the end; the demo owner's password stays in the script's memory.

| Image | What it shows | Size |
| --- | --- | --- |
| `overview.png` | Overview of Example Digital AG | 1440 × 900 |
| `notices.png` | Notices waiting for an answer | 1440 × 900 |
| `reminder.png` | The first open reminder, the critical PHP update of the Webshop | 1440 × 900 |
| `vulnerability.png` | Entry CVE-2026-109619 (PHP) | 1440 × 900 |
| `wizard-stack.png` | The setup wizard at the stack step | 1440 × 900 |
| `phone-overview.png` | Overview at phone width | 390 × 844 |
| `phone-reminder.png` | The same reminder at phone width | 390 × 844 |

Sizes are CSS pixels; the files have twice as many (device scale factor 2), together about 2 MB. The browser runs
in Chromium of the Playwright image, in English, light, with reduced motion, the time zone Europe/Zurich and its
clock two hours after the demo data was loaded. What still changes from run to run comes from the data: dates and
times are those of the run, and `seed-demo` does not always number the reminders in the same order or put the same
of two equally ranked entries first.
