---
name: CSH Alert
description: A security early-warning system that reports risk the way a natural-hazard bulletin does, on a five-level danger scale.
colors:
  paper: "#FFFFFF"
  wash: "#F3F4F5"
  ink: "#1F2326"
  ink-hover: "#111315"
  ink-2: "#4A5056"
  ink-3: "#5F666C"
  rule: "#D7DADD"
  rule-2: "#B3B8BD"
  field: "#767D84"
  on-ink: "#FFFFFF"
  on-ink-2: "#C8CCD0"
  band-hover: "#30353A"
  link: "#0B57B0"
  link-hover: "#083F82"
  select: "#CFE0F7"
  ok: "#17663A"
  l1: "#D9F2A0"
  l2: "#FFD500"
  l3: "#FF9500"
  l4: "#D4111E"
  l5: "#7A0A12"
  tile-edge: "rgba(31, 35, 38, 0.6)"
typography:
  page-title:
    fontFamily: "'Source Sans 3', 'Segoe UI', system-ui, sans-serif"
    fontSize: "1.875rem"
    fontWeight: 900
    lineHeight: 1.15
    letterSpacing: "-0.012em"
  lead-headline:
    fontFamily: "'Source Sans 3', 'Segoe UI', system-ui, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 900
    lineHeight: 1.14
    letterSpacing: "-0.01em"
  section:
    fontFamily: "'Source Sans 3', 'Segoe UI', system-ui, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 900
    lineHeight: 1.2
  reading:
    fontFamily: "'Source Sans 3', 'Segoe UI', system-ui, sans-serif"
    fontSize: "1.0625rem"
    fontWeight: 400
    lineHeight: 1.45
  interface:
    fontFamily: "'Source Sans 3', 'Segoe UI', system-ui, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 400
    lineHeight: 1.5
    fontFeature: "'lnum', 'tnum'"
  meta:
    fontFamily: "'Source Sans 3', 'Segoe UI', system-ui, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.45
  label:
    fontFamily: "'Source Sans 3', 'Segoe UI', system-ui, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 700
    lineHeight: 1.45
  identifier:
    fontFamily: "'Source Code Pro', ui-monospace, 'SFMono-Regular', monospace"
    fontSize: "0.9em"
    fontWeight: 400
  numeral:
    fontFamily: "'Source Sans 3', 'Segoe UI', system-ui, sans-serif"
    fontSize: "1.0625rem"
    fontWeight: 900
    lineHeight: 1
  numeral-lead:
    fontFamily: "'Source Sans 3', 'Segoe UI', system-ui, sans-serif"
    fontSize: "6rem"
    fontWeight: 900
    lineHeight: 1
  numeral-lead-sm:
    fontFamily: "'Source Sans 3', 'Segoe UI', system-ui, sans-serif"
    fontSize: "3.25rem"
    fontWeight: 900
    lineHeight: 1
rounded:
  none: "0"
  control: "2px"
spacing:
  band: "60px"
  band-phone: "52px"
  ladder: "300px"
  ladder-compact: "248px"
  gutter: "48px"
  gutter-compact: "28px"
  gutter-phone: "16px"
  section: "32px"
  section-phone: "26px"
  block: "12px"
  control: "40px"
  control-touch: "44px"
components:
  tile:
    typography: "{typography.numeral}"
    rounded: "{rounded.none}"
    size: "28px"
  tile-md:
    size: "40px"
  tile-lg:
    size: "56px"
  tile-xl:
    typography: "{typography.numeral-lead}"
    size: "132px"
  tile-xl-phone:
    typography: "{typography.numeral-lead-sm}"
    size: "72px"
  tile-l1:
    backgroundColor: "{colors.l1}"
    textColor: "{colors.ink}"
  tile-l2:
    backgroundColor: "{colors.l2}"
    textColor: "{colors.ink}"
  tile-l3:
    backgroundColor: "{colors.l3}"
    textColor: "{colors.ink}"
  tile-l4:
    backgroundColor: "{colors.l4}"
    textColor: "{colors.on-ink}"
  tile-l5:
    backgroundColor: "{colors.l5}"
    textColor: "{colors.on-ink}"
  ladder-step:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    height: "58px"
    padding: "0 14px 0 0"
  ladder-step-active:
    backgroundColor: "{colors.wash}"
  button-primary:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.on-ink}"
    rounded: "{rounded.control}"
    height: "{spacing.control}"
    padding: "0 16px"
  button-primary-hover:
    backgroundColor: "{colors.ink-hover}"
  button-secondary:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    height: "{spacing.control}"
    padding: "0 16px"
  button-secondary-hover:
    backgroundColor: "{colors.wash}"
  button-secondary-active:
    backgroundColor: "{colors.rule}"
  button-text:
    textColor: "{colors.link}"
    rounded: "{rounded.control}"
    height: "{spacing.control}"
    padding: "0 6px"
  button-text-hover:
    textColor: "{colors.link-hover}"
  field:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    typography: "{typography.interface}"
    rounded: "{rounded.control}"
    height: "{spacing.control}"
    padding: "0 12px"
  tag:
    textColor: "{colors.ink}"
    typography: "{typography.meta}"
    rounded: "{rounded.control}"
    height: "22px"
    padding: "0 7px"
  tag-solid:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.on-ink}"
  tag-soft:
    textColor: "{colors.ink-2}"
  band:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.on-ink}"
    height: "{spacing.band}"
    padding: "0 32px"
  nav-link:
    textColor: "{colors.on-ink-2}"
    rounded: "{rounded.control}"
    padding: "8px 14px"
  nav-link-hover:
    backgroundColor: "{colors.band-hover}"
    textColor: "{colors.on-ink}"
  nav-link-current:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
  inline-note:
    backgroundColor: "{colors.wash}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "10px 14px"
  tabbar:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.on-ink-2}"
    height: "56px"
  actionbar:
    backgroundColor: "{colors.paper}"
    padding: "10px 16px"
---

# Design System: CSH Alert

Recorded from the shipped planning-phase mockups in `docs/design/mockups/` (`csh.css`, four pages, finish review disposition: ship). The production frontend will be Angular; the CSS custom properties in `csh.css` are the token source and keep their names.

## Overview

**Creative North Star: "The Hazard Bulletin"**

Risk is reported the way an avalanche or weather service reports it: one numbered danger level, the area it applies to, and the recommended action. The interface borrows four things from that world and nothing else: a humanist sans with black-weight numerals, a five-step danger ramp, a ruled and compact density, and the danger ladder. Navigation, tables and form controls are the standard ones, drawn plainly.

The surface is white paper under an anthracite header band. Structure comes from rules of three fixed weights and from whole columns, never from boxes: there are no cards, no cast shadows, no gradients, and corners are square or at most 2px. Colour is scarce by construction. The five ramp colours appear only where a level is stated; everything else is ink, paper, two greys and one blue for links and focus. Light theme only.

The system is built for two readings. At a desk the bulletin is scanned across projects; on a phone one alert is opened from a notification and has to answer what happened, whether we are affected, and what to do before anything else is seen. Silence is a valid state: when nothing is at level 2 or above, the page says so in a sentence instead of filling the space.

**Key Characteristics:**
- One signature, the danger ladder: filter, count and legend at once.
- Level is a square tile with a black-weight numeral, always accompanied by its word.
- Ramp colours mean level and nothing else.
- Ink rules of 3px, 2px and 1px carry the hierarchy; no cards, no cast shadows.
- One type family in four weights (400, 600, 700, 900), a monospace only for identifiers.
- Solid ink is the primary action; blue is links and focus.
- Motion is state change only, 150ms, nothing on load.

## Colors

A neutral anthracite ink family on white, one interaction blue, and a five-step danger ramp that darkens monotonically from level 1 to level 5.

### Primary
- **Anthracite Ink** (`ink`, #1F2326): body text, the header band, the 3px and 2px rules, the primary action, solid tags, selected borders, error borders and data graphics. 15.8:1 on paper.
- **Pressed Ink** (`ink-hover`, #111315): hover and pressed state of the primary action.

### Secondary
- **Bulletin Blue** (`link`, #0B57B0): links, the focus ring, text buttons and the text caret. 7.0:1 on paper.
- **Deep Bulletin Blue** (`link-hover`, #083F82): hover of links and text buttons.
- **Selection Blue** (`select`, #CFE0F7): text selection background, with ink text.

### Tertiary: the danger ramp
- **Level 5 Oxblood** (`l5`, #7A0A12): Critical. White numeral, 11.2:1.
- **Level 4 Signal Red** (`l4`, #D4111E): High. White numeral, 5.4:1.
- **Level 3 Orange** (`l3`, #FF9500): Medium. Ink numeral, 7.2:1.
- **Level 2 Yellow** (`l2`, #FFD500): Low. Ink numeral, 11.1:1.
- **Level 1 Pale Green** (`l1`, #D9F2A0): Not relevant. Ink numeral, 12.9:1.
- **Tile Edge** (`tile-edge`, ink at 60%): a 1px inset edge on levels 1 to 3, whose fills fall below 3:1 against paper.

### Neutral
- **Paper** (`paper`, #FFFFFF): the ground of every work area, fields, secondary buttons, the phone action bar.
- **Wash** (`wash`, #F3F4F5): row hover, button hover, inline notes, the unknowns block, skeletons.
- **Ink 2** (`ink-2`, #4A5056): secondary text, labels, table heads, meta lines. 8.2:1 on paper.
- **Ink 3** (`ink-3`, #5F666C): placeholders and zero counts. 5.8:1 on paper.
- **Rule** (`rule`, #D7DADD): 1px hairlines between rows and columns.
- **Rule 2** (`rule-2`, #B3B8BD): the border of soft tags, the scrollbar thumb. Decorative strength only (2.0:1); never the only boundary of a control.
- **Field** (`field`, #767D84): the border of inputs and the outline of an empty meter segment. 4.2:1 on paper.
- **On Ink** (`on-ink`, #FFFFFF) and **On Ink 2** (`on-ink-2`, #C8CCD0): text on the header band and the phone tab bar; the second for links at rest.
- **Band Hover** (`band-hover`, #30353A): hover fill on the header band.
- **Confirmation Green** (`ok`, #17663A): the check icon in a confirmation note, and nowhere else. 6.4:1 on Wash, where it sits. It is not a ramp colour and must not be read as level 1.

### Named Rules
**The Level-Only Rule.** The five ramp colours mean level and nothing else. Counts, badges, focus rings, selection, errors, working states, tags and data graphics use ink, paper or blue. Red never means "error" or "status".

**The Numeral-And-Word Rule.** A level is always a numeral plus a word. The tile carries the numeral; the level name (Critical, High, Medium, Low, Not relevant) or a sentence naming the level stands beside it. Colour only repeats what they say.

**The Darkening Ramp Rule.** The ramp darkens step by step from 1 to 5, so the order survives greyscale and colour-blind viewing. Levels 1 to 3 take an ink numeral and the tile edge; levels 4 and 5 take a white numeral and no edge. A new ramp colour must keep that order.

**The Solid Ink Rule.** The primary action is solid ink. Blue is reserved for links, text buttons and focus; it is never a button fill.

## Typography

**Display Font:** Source Sans 3 (with Segoe UI, system-ui, sans-serif)
**Body Font:** Source Sans 3 (the same family)
**Label/Mono Font:** Source Code Pro (with ui-monospace, SFMono-Regular, monospace)

**Character:** A humanist sans used at its extremes: regular for reading, black (900) for every heading and every level numeral, so the page reads like a printed bulletin. Both families are variable WOFF2 files shipped with the application under the SIL Open Font License 1.1; nothing is fetched from a font service. Numerals are lining and tabular everywhere.

### Hierarchy
- **Page title** (900, 1.875rem / 30px, 1.15, -0.012em): one per page, above the 3px ink rule. Drops to 1.5rem at 760px and below; the alert title drops to 1.25rem.
- **Lead headline** (900, 1.5rem / 24px, 1.14, -0.01em): the headline of the bulletin lead. 1.25rem on phones.
- **Section** (900, 1.25rem / 20px, 1.2): section heads inside a page, and the wordmark (with -0.01em).
- **Reading** (400, 1.0625rem / 17px, 1.45 to 1.5): the three answers, the lead columns, assessment prose. Prose is capped at 38rem; table summaries at 62ch.
- **Interface** (400, 600 and 700, 0.9375rem / 15px, 1.5): the body default; tables, controls, navigation. Buttons are 700, navigation, tags and status are 600. Small sub-heads are this size at 900.
- **Meta** (400, 0.8125rem / 13px, 1.45): timestamps, notes, captions, footers, in Ink 2.
- **Label** (700, 0.8125rem / 13px, sentence case, no tracking, Ink 2): table heads, field labels, the heads of the lead columns and of the ladder column.
- **Identifier** (Source Code Pro 400, 0.9em of its context; 13px with -0.02em inside meta lines and breadcrumbs): alert numbers, CVE IDs, versions, vectors.
- **Numeral** (900, line-height 1): the level numeral, on the scale in the three working tiles: 1.0625rem in 28px, 1.5rem in 40px, 1.875rem in 56px.
- **Lead numeral** (900, line-height 1, `--t-numeral-lead` 6rem; `--t-numeral-lead-sm` 3.25rem on phones): the numeral of the bulletin lead tile, the one size outside the scale.

### Named Rules
**The Fixed Step Rule.** Every text size is one of six fixed custom properties (13, 15, 17, 20, 24, 30px). The only size outside the scale is the numeral of the lead tile, which has its own two tokens. No fluid scaling, no `clamp()`; the phone moves a role down a step.

**The Black Or Regular Rule.** Headings and level numerals are 900. Emphasis in running text is 700, controls are 600 or 700. Nothing is set in a light weight, nothing is uppercase, and tracking is only ever tightened slightly on titles and identifiers.

**The Mono Is Identity Rule.** Source Code Pro marks things a person may copy or compare: identifiers, versions, vectors. It is never used for labels, numbers in tables, or atmosphere.

## Layout

The shell is a 60px ink header band over a work area capped at 1440px. List pages are two columns: a 300px ladder column with a 1px right hairline, and a fluid main column padded 32px 48px 72px 44px. The alert detail is a single column capped at 1280px, with its content split into a fluid stack and a 340px side stack (sources, history, note) 56px apart. Vertical rhythm is a flex column: 32px between page sections, 12px inside a section, 36px between detail sections. There is no spacing scale as tokens; the measures above and the 4, 8, 12, 16, 24, 28, 32, 48px steps are what the build reuses.

Density is compact and ruled. Table cells are padded 13px vertically and 20px to the right; a row is a hairline, never a box. Controls are 40px high on desktop and 44px at touch widths.

Reflow happens by whole columns at two breakpoints:

- **1180px and below:** the ladder column narrows to 248px, gutters go to 28px, the detail side stack drops below the main stack, the three answers stack vertically, and the detail actions move under the title.
- **760px and below:** the band shrinks to 52px and keeps only wordmark and tools; primary navigation moves to a fixed four-item tab bar at the bottom; the ladder lies flat across the top as five equal segments; gutters go to 16px; tables marked for stacking become stacked records (level and status on the first line, title and summary full width, secondary cells labelled inline, the date column dropped); the alert detail replaces the tab bar with a fixed action bar. The main column keeps 104px of bottom padding for the fixed bar.

### Named Rules
**The Whole-Column Rule.** Responsive change moves or stacks whole columns and swaps a component for its phone form. Nothing scales fluidly and nothing is hidden that carries an answer.

**The Three Answers Rule.** On a phone alert, level, title, and the three answers (what happened, are we affected, what to do) sit in the first viewport above the action bar. Supporting graphics, dates and long meta give way first.

## Elevation & Depth

Flat. There are no cast shadows, no blur, no gradients and no tonal stacking of surfaces. Separation is done with rules (3px ink under a page title and above the three answers, 2px ink under a table head and above key-value, reference and history lists, 1px hairline between rows) and with one tone, Wash, for hover and notes. The two fixed phone bars separate by fill (ink tab bar) or by a 1px ink top border (paper action bar).

`box-shadow` appears in the build only as an inset edge, never offset or blurred: it draws lines that must not change layout.

### Shadow Vocabulary
- **Tile edge** (`box-shadow: inset 0 0 0 1px rgba(31, 35, 38, 0.6)`): the outline of level 1 to 3 tiles.
- **Selected edge** (`box-shadow: inset 0 0 0 1px #1F2326` on top of a 1px ink border): the selected ladder step and an invalid field, giving a 2px ink frame.
- **Outline segment** (`box-shadow: inset 0 0 0 1px #767D84`, 1.5px ink on the version range): the empty part of a meter or range.
- **Current tab** (`box-shadow: inset 0 3px 0 #FFFFFF`): the marker of the current item in the phone tab bar.

### Named Rules
**The Three Rules Rule.** 3px ink opens a page or its answer block, 2px ink opens a table or list, 1px hairline separates rows. A fourth weight does not exist.

**The Inset-Only Rule.** A shadow is a line drawn inside an element. Any shadow with an offset, a blur or a spread outward is out of the system.

## Shapes

Square. Level tiles, ladder steps, the avatar, the notification count, notes, meter segments and every container have no radius. Interactive controls and tags (buttons, fields, navigation links, band buttons, tags) take a 2px radius, just enough to read as controls; nothing exceeds it. There are no pills, no circles as containers, no clipped or decorative shapes.

Borders are thin and ink-coloured where they mean something: 1.5px ink on buttons, 1px ink on tags and on hover or selection, 1px Field grey on inputs, 1px Rule on ladder steps at rest. Adjacent ladder steps overlap by 1px so the ladder reads as one object; the hovered, selected or focused step rises above its neighbours.

Icons are inline SVG line icons on a 24px grid, drawn at 1.125em with a 1.9 stroke, round caps and joins, in the current text colour. Working states are told apart by icon shape: an empty circle (Open), a clock (In review), a checked circle (Remediated), a flag (Risk accepted), a struck circle (Not affected).

## Components

Each entry below is one Angular component candidate; the class names of the mockup are given only so the CSS can be found. Motion is the same everywhere: `background-color`, `color` and `border-color` transition over 150ms with `cubic-bezier(0.16, 1, 0.3, 1)`; nothing animates on load; under `prefers-reduced-motion` transitions collapse. Focus is a 2px Bulletin Blue outline offset 2px, white on the header band and tab bar.

### Level tile (signature)
A square with a black-weight numeral, centred. Four sizes: 28px (table rows, meta lines, history), 40px (phone alert head), 56px (ladder, alert head), 132px (bulletin lead, with the lead numeral; 72px on phones). Level 1 to 3: ramp fill, ink numeral, inset tile edge. Level 4 and 5: ramp fill, white numeral. The tile is hidden from assistive technology and always stands with its word: `tile + name` at 700 with a 10px gap is the level mark. Inputs: `level` (1 to 5), `size`.

### Level change (signature)
Two 28px tiles joined by a right arrow icon in Ink 2, 6px apart: previous level, arrow, new level. It always travels with a sentence; where that sentence can be hidden or is absent, the mark carries a visually hidden text equivalent ("Raised from level 4 to 5").

### Danger ladder (signature)
Five steps from 5 down to 1, each a full-height 56px tile, the level name and the count (both 700 at Reading size, the count right-aligned). It is filter, count and legend at once.
- **Shape:** square steps, 58px high, 1px Rule border, overlapping by 1px; 300px column.
- **States:** hover takes an ink border; pressed takes a Wash fill; selected takes a 2px ink frame; a zero count is regular weight in Ink 3; focus is the standard ring above its neighbours.
- **Behaviour:** on the alert list each step is a toggle button (`aria-pressed`); pressing one filters the list to that level, pressing it again or the text button under the ladder returns to the default view. The default list shows levels 2 to 5; level 1 appears only when asked for, with an inline note. The count line beside the page title reports the result as a live status, and the level is mirrored in the URL fragment (`#level-5`). On the bulletin each step is a link to the filtered list and nothing is framed: the ink frame means "selected filter" and nothing else. The day's highest step is marked in words, a "Highest today" line (600, Meta size, Ink 2) under the level name.
- **Phone:** five equal segments in one row; each a 44px-high tile over the name (600, Meta size) and the count (Interface size). The "Highest today" line is dropped, because the lead tile directly below says it. Segments are equal whatever the label length, so the bar survives German.

### Buttons
- **Shape:** 2px radius, 40px high (44px at touch widths), 0 16px padding, 1.5px ink border, Interface type at 700, optional leading icon with an 8px gap.
- **Primary:** solid ink with white text; hover and pressed Pressed Ink. One per view.
- **Secondary:** paper with ink text and ink border; hover Wash, pressed Rule.
- **Text:** no border or fill, Bulletin Blue, underlined on hover; for undo and reset. It keeps the full control height, 44px at touch widths.
- **Disabled / Busy:** 45% opacity with a not-allowed cursor; busy keeps its place, swaps its label ("Saving…") and shows a progress cursor.

### Tags
Small bordered signals about a vulnerability or finding, 22px high, 0 7px padding, 2px radius, Meta size at 600. **Outline** (ink border and text) for neutral facts such as "Fix available"; **solid** (ink fill, white text) for the facts that raise urgency, "Exploited in the wild" and "Affected"; **soft** (Rule 2 border, Ink 2 text) for absences such as "No fix listed" and "Unaffected". A finding reads "Affected" or "Unaffected"; "Not affected" is reserved for the working state a person sets. Tags never take a ramp colour.

### Working state
Icon plus word at 600, with an optional assignee in regular Ink 2 after a middle dot ("In review · MK"). Five states, five icon shapes (see Shapes); the icon changes with the state wherever the state is shown, including the one-line status in the phone alert head. Never a coloured dot, never a filled badge.

### Confidence meter
Three 22 by 8px segments, 3px apart, followed by the word: filled ink for reached steps, paper with a Field outline for the rest. The same ink-and-outline grammar draws the version range on the alert (affected span solid, fixed span outlined, a paper marker with an ink outline at the installed version).

### Cards / Containers
There are none. Grouping is done by a heading and a rule. Two tinted blocks exist: the **inline note** (Wash fill, 1px Rule border, 10px 14px padding, 600 weight; a check icon in Confirmation Green for a confirmation with an undo text button, or a 1px ink border and an ink warning icon for a stale source) and the **unknowns block** (Wash fill, 14px 18px padding, no border) that sets assumptions apart from reasoning.

### Inputs / Fields
- **Style:** paper, 1px Field border, 2px radius, 40px high (44px at touch widths), 0 12px padding; a 13px bold label above with a 4px gap. Text areas are padded 10px 12px and resize vertically.
- **Hover / Focus:** ink border on hover; the standard blue focus ring.
- **Error:** a 2px ink frame and an ink message at 13px bold with a warning icon. Errors are not red.
- **Toolbar:** search grows to fill, selects sit beside it; on phones the labels become visually hidden and search takes a full row.

### Tables and lists
- **Table:** 13px bold Ink 2 head over a 2px ink rule; rows padded 13px, separated by hairlines, Wash on hover; numbers right-aligned and tabular; sortable heads are plain buttons with a sort icon and `aria-sort`.
- **Alert row:** level mark, then a 700 ink title that turns blue and underlined on hover, a summary in Ink 2, and a meta line of identifier, tags and any level change.
- **Stacked record (phones):** see Layout.
- **Key-value, references, history:** lists under a 2px ink rule with hairline rows; keys at 700 in a 180px column, references numbered in Ink 2, history entries led by a 13px timestamp and, where the level moved, a level change.
- **List states:** empty is a sentence in the list's place (a 17px black line and one line of context, at most 60ch); loading is skeleton bars in Wash in the row's own shape; a confirmation or a stale source is an inline note above the list.

### Bulletin lead
The highest level of the day: a 132px tile with the level name and its imperative ("Act now") beneath, beside the lead headline, two columns (affected area, recommended action) at Reading size, and one row holding the primary action, the working state and any level change. On phones the tile shrinks to 72px beside its words, the headline drops to 20px and the columns stack.

### Alert summary (the three answers)
Three equal columns under a 3px ink rule, divided by hairlines, each a 17px black question over Reading text: What happened, Are we affected, What to do. Stacked with hairlines between from 1180px down; Interface size on phones. See the Three Answers Rule.

### Navigation
- **Header band:** 60px ink bar (52px on phones, where its buttons are 44px square at least); wordmark at 20px black; links at 600 in On Ink 2, hover white on Band Hover, current page as a paper chip with ink text; on the right the organization switcher, notifications with a square paper count, and a square initials avatar.
- **Phone tab bar:** fixed ink bar of three or four equal items, 56px high plus the safe-area inset, a 22px icon over a Meta-size label at 600; the current item is white with a 3px white top marker.
- **Phone action bar:** on an alert it replaces the tab bar: paper, 1px ink top border, the primary action, the status select and Assign in one row.
- **Breadcrumb:** Meta size, blue 600 link, slash, identifier.

## Do's and Don'ts

### Do:
- **Do** state every level as a tile plus its word, and every level change as two tiles joined by an arrow with a sentence beside it.
- **Do** keep the ramp for levels only; draw counts, selection, errors, tags and data graphics in ink, paper or Bulletin Blue.
- **Do** mark working states with an icon shape and a word.
- **Do** build hierarchy from the three rule weights (3px, 2px, 1px) and from 900-weight headings.
- **Do** use solid ink for the one primary action of a view, and blue for links, text buttons and the 2px focus ring.
- **Do** keep controls 40px high on desktop and 44px at touch widths.
- **Do** reflow by whole columns at 1180px and 760px, and keep the phone ladder as five equal segments.
- **Do** keep level, title and the three answers in the first phone viewport of an alert.
- **Do** ship Source Sans 3 and Source Code Pro with the application.
- **Do** write an empty list as a calm sentence in the list's place.

### Don't:
- **Don't** use a ramp colour for anything that is not a level: no red errors, no green success fills, no yellow warnings, no coloured status dots.
- **Don't** show a level by colour alone, or a tile without its word or sentence.
- **Don't** add cards, cast shadows, gradients, or any radius above 2px.
- **Don't** fill a button with blue, or use a second accent hue.
- **Don't** introduce fluid type, uppercase or letter-spaced labels, or a weight below 400.
- **Don't** set anything but identifiers, versions and vectors in the monospace.
- **Don't** animate on load, or animate anything but background, text and border colour on a state change.
- **Don't** add a dark theme; light is the only theme.
- **Don't** summarise the bulletin as KPI tiles, a severity donut or a feed of pill badges.
- **Don't** load fonts from a font service.
