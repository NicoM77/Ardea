---
name: Ardea
description: A self-hosted security early-warning system that looks and behaves like a well-made system application.
colors:
  canvas: "#F5F5F7"
  surface: "#FFFFFF"
  sidebar: "#EBEBEF"
  fill: "#EDEDF0"
  fill-hover: "#E2E2E7"
  row-hover: "#F7F7F9"
  nav-hover: "rgba(0, 0, 0, 0.045)"
  avatar: "#C7C7CC"
  label: "#1D1D1F"
  secondary: "#5E5E64"
  tertiary: "#A1A1A6"
  on-accent: "#FFFFFF"
  separator: "#E3E3E8"
  border: "#D7D7DC"
  field-border: "#85858B"
  accent: "#0062C4"
  accent-hover: "#00519F"
  accent-soft: "#DCE9FA"
  critical: "#C8102E"
  high: "#B84A00"
  medium: "#8A6100"
  done: "#1E7D34"
typography:
  title:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable Text', 'Segoe UI', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "1.75rem"
    fontWeight: 700
    lineHeight: 1.18
    letterSpacing: "-0.015em"
  headline:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable Text', 'Segoe UI', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "1.0625rem"
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: "-0.005em"
  reading:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable Text', 'Segoe UI', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.5
  body:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable Text', 'Segoe UI', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 400
    lineHeight: 1.47
    fontFeature: "tnum"
  sub:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable Text', 'Segoe UI', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.47
  caption:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable Text', 'Segoe UI', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 600
    lineHeight: 1.47
  mono:
    fontFamily: "ui-monospace, 'SF Mono', 'Cascadia Mono', Consolas, 'Liberation Mono', monospace"
    fontSize: "0.88em"
    fontWeight: 400
rounded:
  group: "12px"
  control: "8px"
  tag: "6px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
  section: "32px"
  gutter: "40px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.on-accent}"
    rounded: "{rounded.control}"
    padding: "0 16px"
    height: "36px"
  button-primary-hover:
    backgroundColor: "{colors.accent-hover}"
    textColor: "{colors.on-accent}"
  button-secondary:
    backgroundColor: "{colors.fill}"
    textColor: "{colors.label}"
    rounded: "{rounded.control}"
    padding: "0 16px"
    height: "36px"
  button-secondary-hover:
    backgroundColor: "{colors.fill-hover}"
  button-plain:
    backgroundColor: "transparent"
    textColor: "{colors.accent}"
    rounded: "{rounded.control}"
    padding: "0 8px"
    height: "32px"
  button-plain-hover:
    backgroundColor: "{colors.accent-soft}"
    textColor: "{colors.accent-hover}"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.label}"
    rounded: "{rounded.control}"
    padding: "0 12px"
    height: "36px"
  group:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.group}"
  list-row-hover:
    backgroundColor: "{colors.row-hover}"
    padding: "12px 16px"
  nav-item:
    backgroundColor: "transparent"
    textColor: "{colors.label}"
    rounded: "{rounded.control}"
    padding: "0 10px"
    height: "34px"
  nav-item-selected:
    backgroundColor: "{colors.accent-soft}"
    textColor: "{colors.label}"
  tag:
    backgroundColor: "{colors.fill}"
    textColor: "{colors.label}"
    typography: "{typography.caption}"
    rounded: "{rounded.tag}"
    padding: "0 8px"
    height: "22px"
  tag-strong:
    backgroundColor: "{colors.label}"
    textColor: "{colors.on-accent}"
    typography: "{typography.caption}"
    rounded: "{rounded.tag}"
  segmented-track:
    backgroundColor: "{colors.fill}"
    padding: "2px"
  segmented-selected:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.label}"
  tabbar:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.secondary}"
    height: "52px"
---

# Design System: Ardea

## Overview

**Creative North Star: "The Well-Made System App"**

Ardea is a security tool that looks and behaves like a native macOS or iOS work application (Settings, Mail, Reminders), carried to the web. It uses familiar controls in their platform form: a grey sidebar with a light-blue selected row, white grouped sections on a grey canvas, inset lists with chevrons, segmented controls, pop-up buttons, and a tab bar on phones. Nothing is invented for decoration. Every screen gives one clear next step and only shows what helps answer three questions: what happened, are we affected, and what do we do.

The interface is calm and dense enough for a working day. Color is scarce on purpose. One blue marks actions, links, focus and selection. Priority is the only other hue, and it never appears without its shaped symbol and its word. Statuses are neutral and told apart by shape. Depth comes from the canvas-to-surface step and from hairlines, not from shadows. Motion is limited to 150ms state changes.

Owner decision, 18 Nov 2026: this world replaces the "Hazard Bulletin" direction. That direction's danger ladder, level tiles, ink header band and Source Sans 3 do not carry over. The interface uses the platform system font. The Inter Tight face belongs to the logo wordmark only. The interface is light-only.

**Key Characteristics:**
- Grey canvas, white 12px-corner groups, hairlines between rows.
- One blue accent for every interactive affordance; nothing else is blue.
- Priority = shaped symbol + word + color, in that order of authority.
- Neutral, shape-coded working states; only Remediated is green.
- System font, tabular figures throughout, monospace only for verbatim strings.
- Sidebar on desktop, top bar plus tab bar on phones, action bar on an action.

## Colors

A neutral grey family modeled on platform system colors, one saturated blue, and a three-hue priority set that appears only beside its symbol and word.

### Primary
- **Action Blue** (accent): primary buttons, links, plain buttons, the focus ring, the selected sidebar icon, the active tab, the caret. 5.9:1 on surface and white text on it is 5.9:1.
- **Pressed Blue** (accent-hover): hover for primary buttons, links and plain buttons (7.8:1 on surface).
- **Selection Wash** (accent-soft): selected sidebar row, text selection, plain-button hover. It is a background only, never text.

### Tertiary (priority and outcome)
- **Critical Red** (critical): the Critical octagon and word. 5.9:1 on surface, 5.4:1 on canvas.
- **High Orange** (high): the High triangle and word. 5.2:1 on surface, 4.8:1 on canvas, 4.9:1 on row hover.
- **Medium Ochre** (medium): the Medium diamond and word. 5.5:1 on surface, 5.1:1 on canvas.
- **Remediated Green** (done): the Remediated check, the done-note icon and the status-line icon only. 5.2:1 on surface.
- Low and Not relevant have no hue of their own. They use Secondary Grey and are told apart by shape.

### Neutral
- **Canvas Grey** (canvas): page background behind groups, phone top bar.
- **Surface White** (surface): groups, inputs, tab bar, action bar, segmented thumb.
- **Sidebar Grey** (sidebar): desktop sidebar background.
- **Fill Grey** (fill) / **Fill Hover** (fill-hover): tags, secondary buttons, segmented track, skeleton bars.
- **Row Hover** (row-hover): hover on clickable rows, priority-bar cells and the sidebar organization switch.
- **Nav Hover** (nav-hover): translucent hover on sidebar items.
- **Label Ink** (label): primary text (16.8:1 on surface, 14.2:1 on sidebar), the organization avatar and the strong tag.
- **Secondary Grey** (secondary): metadata, table heads, field labels, placeholders, inactive icons, Low and Not relevant. 6.4:1 on surface, 5.9:1 on canvas, 5.4:1 on sidebar.
- **Tertiary Grey** (tertiary): row chevrons only. At 2.6:1 it is decorative and must never carry text.
- **Separator** (separator): hairlines between rows inside a group, the 1px grid inside the priority bar.
- **Border** (border): the outline of a group, the sidebar edge, the tab-bar top edge.
- **Field Border** (field-border): input, select and textarea boundaries. 3.7:1 on surface, 3.4:1 on canvas, which meets the 3:1 non-text minimum.
- **Avatar Grey** (avatar): user avatar disc (label text on it is 10.0:1).

### Named Rules
**The One Blue Rule.** Blue means "you can act here". Use it for primary actions, links, focus and selection, and nowhere else. Never use it for data, charts or decoration.

**The Earned Hue Rule.** Apart from the blue, color appears only on a priority, and always with its symbol and its word. Red is Critical, so errors are not red. They use Label Ink, a thicker field border and a sentence.

## Typography

**Display Font:** none. Titles use the system stack.
**Body Font:** platform system font (-apple-system / SF Pro Text, Segoe UI Variable, Segoe UI, Roboto, falling back to Helvetica Neue and Arial).
**Label/Mono Font:** ui-monospace / SF Mono / Cascadia Mono / Consolas, only for strings people copy verbatim, such as a CVSS vector.

**Character:** The platform's own voice. No font is downloaded. On every device the type looks like the operating system it runs on, which is the point. Tabular figures are on everywhere, so identifiers, versions and counts line up.

### Hierarchy
- **Title** (700, 28px / 1.75rem, 1.18, -0.015em, balanced wrap): one page title per page. 26px on phones.
- **Headline** (600, 17px / 1.0625rem, 1.3): section titles above groups, empty-state headings.
- **Reading** (400, 16px / 1rem, 1.5, max 70ch): the three answers, quoted source text and the lede. Drops to 15px on phones.
- **Body** (400 to 600, 15px / 0.9375rem, 1.47): interface default. Row titles and buttons use 600, nav items and statuses 500.
- **Sub** (400, 13px / 0.8125rem): metadata, row subtitles, table heads (600), field labels (600, secondary), timestamps, section notes.
- **Caption** (600, 12px / 0.75rem): tags, avatar initials, footnotes (400).

### Named Rules
**The Platform Voice Rule.** Use the system font stack in the interface. The wordmark's Inter Tight stays inside the logo.

**The Word Is Visible Rule.** A priority or status is always written out. Symbols sit beside the word and never replace it. Priority is never a numeral.

## Layout

Desktop is a fixed 248px sidebar beside a scrolling canvas (main padding 28px top, 40px gutter, 72px bottom). Content columns are capped at 1160px (lists), 1060px (action) or 820px (reading pages). Detail pages use a two-column grid: content plus a 300px sticky inspector, 32px apart. The overview pairs a 1.75fr list with a 1fr feed, 24px apart.

Rhythm is a 4px base. Rows pad 12px vertically and 16px horizontally. Sections sit 32px apart (28px on phones), with the section title 8px above its group and inset 4px to align with the group's corner. Page heads leave 24px below. Controls are 36px high (32px small) and 44px on phones.

Breakpoints:
- **Tablet (760 to 1099px):** the sidebar narrows to 220px and the gutter to 28px. The overview, action and vulnerability grids collapse to one column, and the inspector moves below the content.
- **Phone (up to 759px):** the sidebar is gone. A sticky 52px top bar on the canvas carries the organization switch or the back link, plus the account button. A fixed 52px tab bar (Overview, Actions, Projects, More) sits at the bottom. On an action page it is replaced by an action bar: the primary button (2 parts) beside the status pop-up (1 part). Safe-area insets are respected. The priority bar becomes two columns. List rows wrap their trailing meta below the title, with the chevron pinned right.

Tables respond to their container, not the viewport. When a group holding a stacking table is narrower than 640px, each row becomes a record: the head is visually hidden, one cell sits top right, full-width cells take their own line, low-value columns drop, and the remaining cells get their column name as an inline prefix.

## Elevation & Depth

The system is flat and tonal. Depth is one step: white groups on a grey canvas, outlined in a 1px border, with hairline separators between rows. Hover is a tonal shift, never a lift. The only shadow is a hairline-tight "thumb" shadow on the selected segment of a segmented control, also reused on the skip link when it appears.

### Shadow Vocabulary
- **Thumb** (`box-shadow: 0 1px 2px rgba(0, 0, 0, 0.14), 0 0 0 0.5px rgba(0, 0, 0, 0.06)`): the selected segment in a segmented control, the focused skip link.

### Named Rules
**The Flat Group Rule.** Groups, rows, notes and bars never cast shadows. Separate surfaces with the canvas step, the border and hairlines.

## Shapes

Gently rounded, platform-shaped corners at three steps: groups 12px, controls (buttons, inputs, nav rows, pop-ups) 8px, tags and the back link 6px. The segmented track is 9px around a 7px thumb. Notes are 10px and the organization avatar is 7px. User avatars are circles. The focus ring is a 2px blue outline offset 2px. Inside rows it insets by -2px and loses its radius, so it follows the row edge.

Symbols are drawn on a 24px grid and scale with the text they stand in for (1.07em for priority, 1em for status). Icons are 1.8px line drawings with round caps and joins at 1.125em.

## Components

### Buttons
Quiet and familiar. One filled blue button per view.
- **Shape:** gently rounded (8px), 36px high, 16px side padding, 600 weight. 44px high on phones.
- **Primary:** Action Blue fill, white text. Hover goes to Pressed Blue. Use it once per screen (for example "Take into review").
- **Secondary:** Fill Grey with Label Ink. Hover goes to Fill Hover. Used for Cancel and Save note.
- **Plain:** no fill, blue 500 text, 32px high, 8px padding. Hover adds Selection Wash. Used for Undo and inline actions.
- **Disabled:** 40% opacity, not-allowed cursor.
- **Focus:** the global 2px blue ring at a 2px offset.

### Chips (tags)
- **Style:** 22px high, 6px corners, 12px 600 text. Neutral tags use Fill Grey. A strong tag in Label Ink with white text marks the one fact that raises urgency ("Exploited in the wild").
- **State:** static. Tags are not interactive.

### Cards / Containers (groups)
- **Corner Style:** 12px, with content clipped.
- **Background:** Surface White on Canvas Grey.
- **Shadow Strategy:** none (see Elevation & Depth).
- **Border:** 1px Border. Rows inside are divided by 1px Separator hairlines.
- **Internal Padding:** 12px by 16px for rows, 16px by 20px for reading answers, 11px by 16px for property rows.

### Inputs / Fields
- **Style:** Surface White, 1px Field Border, 8px corners, 36px high, 12px padding. Selects are pop-up buttons with an up-down chevron at the right. Textareas start at 76px. Search fields carry a leading magnifier.
- **Labels:** above the field, 13px 600 in Secondary Grey.
- **Hover:** the border darkens to Label Ink.
- **Focus:** the border turns blue and a 2px blue outline sits 1px outside it.
- **Error:** the border goes to Label Ink with a 1px inset ring, and a 13px 600 sentence sits below the field. Never red.

### Navigation
- **Sidebar:** Sidebar Grey with a right border. The logo sits on top, then the organization switch (a white bordered pop-up row), then nav rows: 34px high, 8px corners, 500 weight, grey icons, counts right-aligned in Sub. Hover is Nav Hover. The selected row is Selection Wash with 600 text and a blue icon. Team, Settings and the user sit in a footer above a border.
- **Phone top bar:** sticky, on the canvas, 52px. It holds the organization switch or the back chevron with the parent's name, plus an account avatar with a 44px target.
- **Tab bar:** white with a top border, four equal tabs, 24px icons over 11px 500 labels in Secondary Grey. The active tab turns blue.
- **Back link:** a blue chevron-left plus the parent name, above the title on desktop and in the top bar on phones.

### Segmented Control
A Fill Grey track with 2px padding and a 9px radius. Segments are 32px high, 14px 500 text, with counts in Secondary Grey. The selected segment is a white thumb with the Thumb shadow and 600 weight. Hover shows a translucent white. On phones it fills the width.

### Priority and Status (signature)
- **Priority:** an inline symbol (1.07em) plus a 600 word, both in the priority color. The shapes are a filled octagon with an exclamation mark (Critical), a filled triangle with an exclamation mark (High), a filled diamond with an exclamation mark (Medium), an outlined circle with a down arrow (Low) and an outlined circle with a dash (Not relevant, 500 weight). A change is shown as two priorities joined by a grey arrow, with "raised to" for screen readers.
- **Status:** a neutral symbol (1em, Secondary Grey) plus a 500 word in Label Ink, with the assignee in a Secondary Grey suffix. The shapes are an empty ring (Open), a half-filled ring (In review), a filled check disc in green (Remediated, the only colored status), a flag (Risk accepted) and a struck ring (Not affected).

### Inset List Row
The row title is 600, with a 13px subtitle line carrying priority, project and identifiers. Trailing meta (due date, status) is right-aligned in 13px, followed by a 16px Tertiary Grey chevron. Hover tints the row Row Hover and turns the title blue. The focus ring sits inset. Loading shows skeleton bars in Fill Grey in the row's own shape, with no spinner. The "show more" row is a blue 500 title.

### Priority Bar
One group split into four cells on a 1px Separator grid (two columns on phones). Each cell is a link holding a priority label and a 24px 600 count with a Sub unit. It filters the actions list.

### Notes and Feedback
A confirmation note is a white bordered 10px-radius strip with a leading icon (green check when done), the sentence, and a plain Undo button. Changes are confirmed in place with Undo, never in a dialog. A plain note is a 13px Secondary Grey line with an info icon. Empty states are a centered headline plus one Secondary Grey sentence inside a group.

### Inspector
A sticky column of groups: a property list (Secondary Grey terms left, 500 values right, small pop-ups inline), then the reason form and the note form.

## Do's and Don'ts

### Do:
- **Do** put content in white groups on the grey canvas, with 12px corners, a 1px border and hairlines between rows.
- **Do** give each screen exactly one primary blue button. Everything else is secondary or plain.
- **Do** show priority as symbol, word and color together: octagon Critical, triangle High, diamond Medium, circle-with-arrow Low, circle-with-dash Not relevant.
- **Do** tell statuses apart by shape and keep them grey. Only Remediated is green.
- **Do** use tabular figures for counts, versions and identifiers, and use monospace only for strings people copy verbatim.
- **Do** let tables stack into records when their group is narrower than 640px, using a container query rather than a viewport query.
- **Do** confirm changes in place with Undo, and limit motion to 150ms color and background transitions.
- **Do** keep text at 4.5:1 or better on every surface it sits on, and field boundaries at 3:1 or better.

### Don't:
- **Don't** use blue for anything that is not an action, link, focus or selection.
- **Don't** color errors red. Red means Critical. State the error in words next to the field.
- **Don't** show priority as a numeral, a level number or a colored tile, and don't show any priority without its word.
- **Don't** add shadows to groups, rows or notes, or lift elements on hover.
- **Don't** download a web font for the interface or use the wordmark face outside the logo.
- **Don't** use Tertiary Grey for text. It is for chevrons only.
- **Don't** bring back the rejected Hazard Bulletin vocabulary: the danger ladder, square level tiles, the ink header band or Source Sans 3.
- **Don't** build a dark theme or a dark security-dashboard look. Ardea is light only.
