---
name: Ardea
description: A self-hosted security early-warning system whose screens answer with charts first and words second, on a calm framed sheet.
colors:
  ground: "#F1F4F6"
  surface: "#FFFFFF"
  raised: "#F5F7F8"
  active: "#E4E9EC"
  border: "#E3E8EB"
  rule: "#EDF0F2"
  control: "#C9D0D4"
  field: "#7F868B"
  text: "#131416"
  muted: "#53595D"
  faint: "#656B6F"
  accent: "#1B6C80"
  accent-soft: "#E2EEF1"
  primary: "#131416"
  primary-ink: "#FFFFFF"
  critical: "#C8102E"
  high: "#B84A00"
  medium: "#8A6100"
  low: "#53595D"
  positive: "#1E7B4F"
  positive-soft: "#DCFAE5"
  positive-ink: "#04250E"
  dot: "#B4BDC2"
  dot-faint: "#DFE4E7"
typography:
  figure:
    fontFamily: "'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "36px"
    fontWeight: 500
    lineHeight: "40px"
    letterSpacing: "-0.02em"
  number:
    fontFamily: "'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "28px"
    fontWeight: 500
    lineHeight: "32px"
    letterSpacing: "-0.01em"
  title:
    fontFamily: "'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "22px"
    fontWeight: 600
    lineHeight: "28px"
    letterSpacing: "-0.01em"
  section:
    fontFamily: "'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "16px"
    fontWeight: 600
    lineHeight: "24px"
  body:
    fontFamily: "'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: "20px"
    fontFeature: "tnum"
  sub:
    fontFamily: "'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: "18px"
  small:
    fontFamily: "'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: "16px"
  mono:
    fontFamily: "'IBM Plex Mono', ui-monospace, 'Cascadia Mono', Consolas, monospace"
    fontSize: "13px"
    fontWeight: 400
rounded:
  sheet: "16px"
  card: "8px"
  control: "6px"
  pill: "999px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  card: "20px"
  gap: "24px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.primary-ink}"
    rounded: "{rounded.control}"
    padding: "0 12px"
    height: "32px"
  button:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.control}"
    padding: "0 12px"
    height: "32px"
  button-hover:
    backgroundColor: "{colors.raised}"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.control}"
    padding: "0 10px"
    height: "32px"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.card}"
    padding: "20px"
  sheet:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.sheet}"
    padding: "24px"
  nav-item:
    backgroundColor: "transparent"
    textColor: "{colors.muted}"
    rounded: "8px"
    padding: "0 8px"
    height: "32px"
  nav-item-selected:
    backgroundColor: "{colors.active}"
    textColor: "{colors.text}"
  chip:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.muted}"
    rounded: "{rounded.pill}"
    padding: "0 10px"
    height: "32px"
  chip-selected:
    backgroundColor: "{colors.active}"
    textColor: "{colors.text}"
  pill:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    typography: "{typography.small}"
    rounded: "{rounded.pill}"
    padding: "0 8px"
    height: "22px"
  toast:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.primary-ink}"
    rounded: "10px"
    height: "44px"
---

# Design System: Ardea

## Overview

**Creative North Star: "The answer is drawn"**

Every screen answers its one question with a picture before it uses a sentence: deadlines are bars on a shared time axis, the week's CVE records are a field of dots, "are we affected" is a pin inside a version range, and a priority is a bar of points. Words are short labels around those pictures. The chrome is quiet so the data can carry the colour: a grey-blue frame holds the sidebar and the top bar, and the work sits on a white sheet inset 8px, with no shadow.

Owner decision, 18 Nov 2026: this direction replaces "The Well-Made System App". The owner found that version too wordy and too plain and asked for charts, little text and smooth motion, with freedom to restructure. Three earlier rules are withdrawn with it: "no charts", "nothing moves on load" and "system font only". What stays: light only, calm surfaces, priority as symbol plus word plus colour, WCAG 2.2 AA, and nothing from the rejected "Hazard Bulletin".

**Key characteristics:**
- A tinted frame, a white sheet, cards with a 1px edge and 8px corners. No shadow on anything at rest.
- Colour sits on data. The frame, the navigation and the controls are grey; the one filled button is near-black.
- One data hue, the heron's slate teal, plus the three priority hues.
- Priority is shaped symbol, word and colour, in that order of authority.
- IBM Plex Sans for the interface, IBM Plex Mono for identifiers and versions.
- Charts draw themselves once on arrival. After that, motion is feedback.

## Colors

### Neutral
- **Ground** (ground): the frame behind the sheet, sidebar and top bar. A grey with a blue step, taken from the heron.
- **Surface** (surface): the sheet, cards, inputs, the selected segment.
- **Raised** (raised): hover on rows, wells, the segmented track, neutral pills.
- **Active** (active): the selected navigation item and a pressed filter chip.
- **Border** (border): the edge of the sheet and of cards. **Rule** (rule): lines between rows, one step lighter.
- **Control** (control): the edge of outlined buttons and chips, which carry a label.
- **Field** (field): the boundary of inputs, selects and textareas. 3.7:1 on the sheet, above the 3:1 non-text minimum.
- **Text** (text): 18.4:1 on the sheet. **Muted** (muted): labels and secondary text, 7.1:1. **Faint** (faint): meta, axis ticks and placeholders, 5.4:1.

### Data
- **Accent** (accent): the one chart hue. The trend line, the dots that name a profiled technology, the score blocks, the focus ring. 6.0:1 on the sheet. It is not a button colour.
- **Dot** / **Dot faint**: a CVE record that has not been sorted yet, and one that was dismissed.

### Priority and outcome
- **Critical** (critical) 5.9:1, **High** (high) 5.2:1, **Medium** (medium) 5.5:1. **Low** and **Not relevant** use Muted and are told apart by shape.
- **Positive** (positive): remediated, fixed versions, time-to-close bars. 5.2:1 as text.

### Named rules
**Colour on data.** A hue appears on a chart mark, a priority or an outcome. Navigation, tabs, chips and buttons are grey or near-black.

**The earned red.** Red means Critical. Errors are not red: they use Text, a second line inside the field border and a sentence.

**People are tints.** Each person has a pale tint of their own for the avatar. Tints are identity, not data, and never appear in a chart.

## Typography

**Interface:** IBM Plex Sans, weights 400, 500 and 600, loaded as a web font, with the platform's system font as the fallback.
**Identifiers:** IBM Plex Mono for CVE identifiers, versions and the CVSS vector, the strings people compare and copy.
The Inter Tight wordmark stays inside the logo.

Tabular figures are on everywhere.

### Hierarchy
- **Figure** (36 / 500): the screen's answer when it is a number, such as the time left. One per screen. 32px on phones.
- **Number** (28 / 500): a count that leads a card.
- **Title** (22 / 600): one per page.
- **Section** (16 / 600): card titles.
- **Body** (14 / 400 to 500): interface default. Row titles and buttons use 500.
- **Sub** (13 / 400): the second line of a row, sources, time stamps.
- **Small** (12 / 400): card meta, axis ticks, pills.

### Named rules
**The word is visible.** A priority or status is always written out. Symbols sit beside the word and never replace it. Priority is never a numeral.

**Units are quieter than figures.** "1 d 13 h 28 m" sets the numbers in Text and the units in Muted at 16px.

## Layout

Desktop: a 228px sidebar and a 48px top bar on the ground, with no line between them. The sheet is inset 8px from the right and bottom edges, has a 1px edge and 16px corners, and pads its content by 24px. Content is capped at 1240px.

One 12-column grid with one 24px gutter. Blocks pair as 8 + 4, 7 + 5 or 9 + 3; a side column stacks its cards with the same gap. At 1440px the sums close: 761 + 24 + 369 = 1154.

Breakpoints:
- **Up to 1100px:** every block spans the grid. A side column dissolves into it, and the card with the next step moves to the top.
- **Up to 900px:** the frame folds. The sidebar becomes a full-screen menu behind a button; the top bar keeps the mark, the search and the account.
- **Up to 640px:** 16px padding, 40 to 44px controls. Table rows become two-line records. The deadline chart puts each bar under its action. An action page gets a fixed bar with its primary button and status.

## Elevation and Depth

Flat. Depth is one tone step (ground to sheet) and 1px edges. Shadows belong only to what floats: the tooltip, the confirmation toast and the search dialog.

## Shapes

Sheet 16px, cards 8px, controls 6px, pills and chips fully round. Chart bars are 12px tall with 3px corners; meters are 4px. The focus ring is 2px Accent, offset 2px. Icons are 1.6px line drawings on a 24px grid at 16px.

## Charts

Each chart answers one question and is drawn from the dataset.

- **Deadline runway.** Fourteen days on one axis, today near the middle, weekends one tone darker. A bar runs from the day an action was created to its deadline: the solid part is time used, the pale part time left. A 2px tick marks the deadline. A bar that ends beyond the axis runs to its edge.
- **Priority ring.** A thin ring, one segment per priority, the total in the middle. Its legend is the list of counts and filters the action list. Hovering a legend row dims the other priorities in the runway.
- **The week's records.** One dot per published CVE record. Three steps, each a button: all records, the ones naming a profiled technology, the ones that hit an installed version. The last are coloured by priority and link to their action.
- **Open actions.** A stepped line over 30 days, the last value labelled.
- **Priority score.** One block per point on a scale that ends at the highest possible sum, with the level bands underneath. The factor that changed last sits at the end, so an earlier state is the same bar without it.
- **Version track.** The affected range as a pale red band, the installed version as a pin inside it, the fixed release as a green check.
- **Severity.** The CVSS score on a half arc, and the vector as one small bar per metric.

### Named rules
**One hue per chart.** A chart uses Accent, or the priority colours when priority is what it shows. Never a colour per bar.

**The swatch is the mark.** A legend shows the mark itself, scaled down.

**No chart without its number.** The figure a chart leads to is printed beside it.

## Motion

- **Easing.** `cubic-bezier(0.23, 1, 0.32, 1)` for anything arriving or responding; `cubic-bezier(0.77, 0, 0.175, 1)` for things that move across the screen, such as the tab indicator. Never ease-in.
- **Durations.** Press 140ms, hover 150ms, interface changes 220ms. A chart drawing itself takes 600 to 900ms, once.
- **Arrival.** Cards rise 8px and fade in, 45ms apart. Bars grow from their left edge, lines draw, numbers count up. This happens once per page view and never holds content back.
- **Feedback.** Buttons scale to 0.97 on press. Hover effects apply only where a pointer can hover.
- **Page to page.** The frame stays still; the sheet's content fades out in 120ms and the new content rises in over 260ms. A row's title travels into the next page's title.
- **Inside a page.** Filtered rows glide to their new place.
- **Only transform and opacity** are animated. Bars grow with scale, never with width.
- **The keyboard gets no animation.** Search opened with `/` simply appears; opened with the pointer it scales in from 0.97.
- **Reduced motion** keeps short fades and drops travel, drawing and pulsing.

## Components

### Buttons
32px high, 6px corners, weight 500. **Primary:** near-black with white text, once per screen. **Default:** white with a Control edge. **Ghost:** no edge. **Disabled:** Raised fill, Faint text.

### Filter chips
A toggle each, fully round, 32px, with the priority's own symbol and a count of the rows it would leave. Pressed: Active fill.

### Tabs and segments
Underline tabs sit on a full-width rule, with one 2px indicator that slides between them. A segmented control has a Raised track and a white thumb that slides.

### Fields
White, 1px Field edge, 6px corners, 32px. Hover darkens the edge to Text; focus shows the Accent ring. An error adds a second line in Text and a sentence underneath.

### Cards
White, 1px Border, 8px corners, 20px padding, a 16px title and optional meta on the right. Rows inside are divided by 1px Rules.

### Priority and status (signature)
- **Priority:** a shaped symbol plus a 500 word, both in the priority colour: filled octagon (Critical), filled triangle (High), filled diamond (Medium), outlined circle with a down arrow (Low), outlined circle with a dash (Not relevant).
- **Status:** a neutral symbol plus the word: empty ring (Open), half-filled ring (In review), green check disc (Remediated), flag (Risk accepted), struck ring (Not affected).
- **Pills:** "Exploited" is a pale red pill with a red dot; "Fix available" pale green; anything else Raised.

### Navigation
Sidebar on the ground: logo, organization switch, items at 32px with 16px icons. The selected item has an Active fill. A count appears only on a queue that needs action. The top bar holds the search, which opens with `/` or Ctrl K.

### Feedback
A change is confirmed by a dark toast at the bottom with Undo, never by a dialog. Tooltips wait 350ms for the first and open at once for the next.

## Do's and Don'ts

### Do
- **Do** answer with a chart and print its number beside it.
- **Do** keep colour on data, priorities and outcomes.
- **Do** show priority as symbol, word and colour together.
- **Do** keep text at 4.5:1 or better and field boundaries at 3:1 or better.
- **Do** animate only transform and opacity, with the two easing curves above.
- **Do** build every figure from the dataset, so a number shown twice is one value.

### Don't
- **Don't** put a hue on navigation, tabs, chips or buttons.
- **Don't** colour errors red. Red means Critical.
- **Don't** show priority as a numeral or without its word.
- **Don't** add shadows to cards or rows, gradients, glows or glass.
- **Don't** put a sparkline or an icon in a tinted square on a number.
- **Don't** animate a keyboard action, or loop an animation that carries no meaning.
- **Don't** build a dark theme. Ardea is light only.
- **Don't** bring back the Hazard Bulletin vocabulary: the danger ladder, level tiles, the ink header band.
