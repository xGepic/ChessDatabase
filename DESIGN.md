---
name: Chess Database
description: A personal chess database for one club player; the Owner's own games, reviewed on a cream-and-brown demonstration board in a brown analysis room, in the Owner's three-colour palette of brown, cream and dark green.
colors:
  brand-brown: "#3b2a20"
  brand-cream: "#efe6d8"
  brand-green: "#173b2c"
  shell: "#22170f"
  ground: "#3b2a20"
  sunken: "#241911"
  panel: "#2d2017"
  panel-hover: "#392a1f"
  raised: "#46342a"
  rule: "#4a3829"
  rule-strong: "#634c3a"
  chalk: "#efe6d8"
  chalk-2: "#cbbda8"
  chalk-3: "#a5947e"
  square-light: "#efe6d8"
  square-dark: "#9a7457"
  select: "#173b2c"
  select-hover: "#1f4d3a"
  select-ink: "#efe6d8"
  green-edge: "#2d6b51"
  accent: "#6fb592"
  engine: "#8cc7a8"
  engine-arrow: "#1f6b4a"
  good: "#8fc29a"
  mark-inaccuracy: "#ecc65a"
  mark-mistake: "#e38a4c"
  mark-blunder: "#e85a3f"
  danger: "#e85a3f"
  rail-white: "#6e5644"
  rail-black: "#574334"
  graph-white: "#d9cdb9"
  score-drawn: "#85725f"
typography:
  display-number:
    fontFamily: "Cascadia Mono, Cascadia Code, Consolas, ui-monospace, monospace"
    fontSize: "26px"
    fontWeight: 600
    lineHeight: 1
    letterSpacing: "-0.01em"
    fontFeature: "tnum"
  headline:
    fontFamily: "Segoe UI Variable Text, Segoe UI Variable, Segoe UI, system-ui, sans-serif"
    fontSize: "18px"
    fontWeight: 600
    lineHeight: 1.3
  title:
    fontFamily: "Segoe UI Variable Text, Segoe UI Variable, Segoe UI, system-ui, sans-serif"
    fontSize: "15px"
    fontWeight: 600
    lineHeight: 1.3
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI Variable, Segoe UI, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.45
  label:
    fontFamily: "Segoe UI Variable Text, Segoe UI Variable, Segoe UI, system-ui, sans-serif"
    fontSize: "12.5px"
    fontWeight: 400
    lineHeight: 1.45
  move:
    fontFamily: "Cascadia Mono, Cascadia Code, Consolas, ui-monospace, monospace"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: "20px"
    fontFeature: "tnum"
  coordinate:
    fontFamily: "Cascadia Mono, Cascadia Code, Consolas, ui-monospace, monospace"
    fontSize: "15px"
    fontWeight: 600
rounded:
  board: "2px"
  control: "3px"
  panel: "4px"
  pill: "10px"
  round: "50%"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "20px"
  gutter: "24px"
components:
  button:
    backgroundColor: "{colors.panel-hover}"
    textColor: "{colors.chalk}"
    rounded: "{rounded.control}"
    padding: "5px 12px"
  button-hover:
    backgroundColor: "{colors.raised}"
  button-primary:
    backgroundColor: "{colors.select}"
    textColor: "{colors.select-ink}"
    rounded: "{rounded.control}"
    padding: "5px 12px"
  button-primary-hover:
    backgroundColor: "{colors.select-hover}"
  button-quiet:
    backgroundColor: "transparent"
    textColor: "{colors.chalk-2}"
    rounded: "{rounded.control}"
    padding: "5px 10px"
  button-quiet-hover:
    backgroundColor: "{colors.panel-hover}"
    textColor: "{colors.chalk}"
  icon-button:
    backgroundColor: "transparent"
    textColor: "{colors.chalk-2}"
    width: "34px"
    height: "32px"
  input:
    backgroundColor: "{colors.sunken}"
    textColor: "{colors.chalk}"
    rounded: "{rounded.control}"
    padding: "5px 8px"
  panel:
    backgroundColor: "{colors.panel}"
    rounded: "{rounded.panel}"
  shell-bar:
    backgroundColor: "{colors.shell}"
    height: "48px"
    padding: "0 24px"
  shell-tab:
    textColor: "{colors.chalk-2}"
    rounded: "{rounded.control}"
    padding: "6px 12px"
  shell-tab-active:
    backgroundColor: "{colors.select}"
    textColor: "{colors.select-ink}"
  panel-tab-active:
    textColor: "{colors.chalk}"
    padding: "8px 12px 9px"
  segmented-on:
    backgroundColor: "{colors.select}"
    textColor: "{colors.select-ink}"
  scoresheet-move:
    textColor: "{colors.chalk}"
    typography: "{typography.move}"
    rounded: "{rounded.board}"
    padding: "3px 8px"
  scoresheet-move-current:
    backgroundColor: "{colors.select}"
    textColor: "{colors.select-ink}"
  tag:
    backgroundColor: "{colors.raised}"
    rounded: "{rounded.pill}"
    padding: "1px 2px 1px 8px"
  engine-switch:
    backgroundColor: "{colors.sunken}"
    rounded: "{rounded.pill}"
    width: "36px"
    height: "20px"
  engine-switch-on:
    backgroundColor: "{colors.engine}"
  board-frame:
    backgroundColor: "{colors.panel}"
    rounded: "{rounded.panel}"
    padding: "14px 14px 30px 40px"
  rail-marker:
    backgroundColor: "{colors.rail-white}"
    rounded: "{rounded.round}"
    width: "clamp(4px, (100cqi - 24px) / moves - gap, 13px)"
    height: "clamp(4px, (100cqi - 24px) / moves - gap, 13px)"
  rail-marker-black:
    backgroundColor: "{colors.rail-black}"
  rail-marker-inaccuracy:
    backgroundColor: "{colors.mark-inaccuracy}"
  rail-marker-mistake:
    backgroundColor: "{colors.mark-mistake}"
  rail-marker-blunder:
    backgroundColor: "{colors.mark-blunder}"
---

# Design System: Chess Database

## Overview

**Creative North Star: "The Analysis Room"**

The Owner's game hangs on the wall of the club's analysis room: a big cream-and-brown demonstration board in a dark brown frame, talked through and marked up after the round. The whole world is built from the Owner's three pinned colours: brown #3B2A20 is the room, cream #EFE6D8 is the text and the light squares, and dark green #173B2C is the felt of selection: the active tab, the current move and the primary action. Every other value is derived from those three. Colour is flat and unmodulated everywhere, and there are no shadows. Depth comes from a few close browns and thin rules. The few marks that carry meaning each have one colour: dark green fills for the thing you are on, a lighter green of the same hue for focus and hover lines, mint for what the engine says, and a ramp from yellow through orange to red for how bad a move was.

The interface is dense and built for one experienced user at a large monitor in a dim room. The board is always the largest element on the review screen and is sized from the viewport. Panels are plain brown tiles with ruled heads, set slightly darker than the ground. Tables and lists are ruled, not boxed. Every number (evaluations, Elo, dates, move numbers, coordinates) is set in a tabular monospace, so the board's world of notation reads as one voice against the humanist UI sans.

It refuses the grey analysis-site panel look and ChessBase's toolbar clutter: tools sit as quiet icon buttons in one row under the board, not as a ribbon.

**Key Characteristics:**
- The Owner's three colours (brown #3B2A20, cream #EFE6D8, dark green #173B2C) are the source of every token.
- Flat brown tones (#22170f to #46342a) separated by 1px rules; no shadows, no texture.
- Dark green fills with cream ink for current, active and primary, each edged in a 1px green line.
- A lighter green of the same hue for focus rings, underlines, input focus and hover text.
- Mint for everything the engine says; a yellow, orange, red ramp for how bad a move was.
- Numbers and moves in Cascadia Mono, tabular; UI in Segoe UI Variable.
- Small radii (2-4px); full rounding only for tags, the switch, dots and the round move-rail markers.
- Every board hangs in a 4px brown frame with large mono coordinates outside the squares.

## Colors

The Owner's three-colour palette, with every other value derived from it.

### Brand Palette (pinned by the Owner)
- **Brand Brown** (brand-brown): the room. It is the page ground, and every other brown (shell, sunken, panel, raised, rules, board's dark square) is a shade of it.
- **Brand Cream** (brand-cream): the primary text and the board's light square. Secondary and tertiary text are cream dimmed toward brown.
- **Brand Green** (brand-green): the selection fill. Its lighter tones (select-hover, green-edge, accent, engine-arrow) keep the same hue.

### Primary
- **Deep Green** (select): fill only, always with cream ink (select-ink). Active shell tab, current scoresheet move, primary button, segmented "on", annotation symbol "on", picker and context-menu highlight, position-editor palette "on". Hover goes to select-hover. The active tab, primary button and current move carry a 1px Green Edge (green-edge) as a border or inset ring, so the dark fill holds its shape on dark brown; the primary button's edge brightens to Light Green on hover.
- **Light Green** (accent): the same hue lifted for lines and text: the focus outline, the active panel tab's underline, input focus borders, link underline on hover, link-button, tree-move and classified-move hover text, checkbox and progress accents, and the current marker on the eval graph. At low alpha it tints picked rows (10%), the explore bar (10% fill, 45% border) and the current classified row (14%).

### Secondary
- **Mint** (engine): everything the engine contributes in the interface. The big eval figure, engine line scores, engine moves in variations and best lines, the "analysed" mark in the game list, the engine switch when on, chart lines and points, and the dashed outline on a Repertoire deviation.
- **Forest Green** (engine-arrow): the engine's best-move arrow on the board (opacity 0.85), dark enough to hold on the cream squares.

### Tertiary
- **Classification Ramp** (mark-inaccuracy, mark-mistake, mark-blunder): a warm yellow, an orange and a hot red. Applied as the move's text colour with a 2px underline in the scoresheet, as dots in the classified list and eval graph, and as round rail markers that also grow with severity.
- **Sage** (good): success and positive verdicts: saved status, board notes, won share in score bars, tablebase wins, right answers in Training (12% fill behind).
- **Hot Red** (danger): errors, destructive buttons' text and hover border, lost share in score bars, tablebase losses, wrong answers in Training. Same value as blunder.

### Neutral
- **Shell Brown** (shell): a deep brown; the top bar, the black share of the eval bar, black colour dots.
- **Ground Brown** (ground): the page background; this is Brand Brown itself, lighter than the panels so panels read as recessed tiles.
- **Sunken Brown** (sunken): inputs, the idle eval bar, best-line wells, the black area of the eval graph.
- **Panel Brown** (panel), **Panel Hover** (panel-hover), **Raised Brown** (raised): panels and board frames, then hover and default button fills, then menus, pickers, tags and tooltips.
- **Rule** and **Strong Rule**: 1px dividers inside panels; control borders, menu borders and scrollbar thumbs.
- **Cream**, **Cream 2**, **Cream 3** (chalk, chalk-2, chalk-3): primary text; secondary text, labels and coordinates; tertiary text, move numbers, placeholders, axis ticks.
- **Board Squares** (square-light cream, square-dark brown): the board only, drawn as a repeating conic pattern. The light square also backs promotion choices.
- **Rail Neutrals** (rail-white, rail-black): unclassified White and Black markers on the move rail.
- **Graph White** (graph-white) and **Drawn** (score-drawn): White's area in the eval graph and the drawn share in score bars. The highlighted chart point is cream.

### Named Rules
**The Three Colours Rule.** Every colour is Brand Brown, Brand Cream or Brand Green, or a shade derived from one of them. The only exceptions are the functional marks: engine, good and the classification ramp.

**The One Meaning Rule.** Deep Green (with Light Green for its lines) means "current or chosen", Mint means "the engine says", the warm ramp means "how bad". Never use one for another's job.

**The Flat Colour Rule.** Surfaces are single flat colours: no grain, texture or gradients. Tints are an accent at low alpha (10-14% fills, 45% borders), never gradients.

## Typography

**UI Font:** Segoe UI Variable Text (with Segoe UI, system-ui)
**Number / Move Font:** Cascadia Mono (with Cascadia Code, Consolas, ui-monospace)

**Character:** A quiet Windows-native sans for the interface, paired with a sturdy programmer's mono that carries everything countable, so notation and figures line up in columns like a scoresheet.

### Hierarchy
- **Display number** (600, 26px, line-height 1, -0.01em, mono): the live engine eval. Related figures use the same treatment at 22-30px: Accuracy (22px), stat tiles (24px), the head-to-head score (30px).
- **Headline** (600, 18px, 1.3): page-level panel heading.
- **Title** (600, 15px, 1.3): panel headings.
- **Body** (400, 14px, 1.45): default text; buttons drop to 13.5px on a 20px line.
- **Label** (400, 12.5px): table headers (600), form labels in filters, hints, sheet metadata, field errors. 12px for chart ticks and assessment meta.
- **Move** (400, 14px on 20px, mono): scoresheet moves; variations 13px. The current move goes 700.
- **Coordinate** (600, 15px, mono): board coordinates, outside the squares, in Cream 2.

### Named Rules
**The Tabular Rule.** Every number (eval, Elo, dates, move numbers, results, scores, ECO codes) is in the mono with tabular figures. Column headers above numbers stay in the UI font.

**The No Uppercase Rule.** No letter-spaced uppercase labels; hierarchy comes from weight and Cream 2/3.

## Layout

The app is a sticky 48px shell bar (brand, then text tabs) over a page with 20px top, 24px side and 28px bottom padding; 12px at phone width.

The review screen is a three-column grid with 20px gaps: the board column, a 320px scoresheet panel, and a side column (min 320px). The board is a square computed from the viewport (`max(360px, min(100vh - 226px, 100vw - 828px, 860px))`), so it is always the largest element; when the Game Analysis strip is showing, its 44px is taken from the board height. Under the board sit the move rail and one row of tool buttons. The scoresheet is sticky (top 68px) and scrolls internally. The side column stacks the Engine panel over a tabbed panel (Game Analysis, Opening Tree, Details) with 16px between.

Below 1180px the side column drops under the board and becomes an auto-fit grid of 320px columns; below 760px everything is one column and the scoresheet stops being sticky. Training uses the same viewport-sized board beside a 320-460px panel.

List screens cap at 1100px (1280px wide variant), centred; forms at 600px. Statistics and Dossier tile their panels edge to edge in a grid with 1px gaps over the rule colour, inside one 4px-rounded frame, so modules read as a ruled sheet.

Spacing runs on 4px steps: 4, 8, 12, 16, 20, 24. Panel heads pad 14px 16px 12px; panel bodies 10px 16px 16px; table cells 9px 16px.

## Elevation & Depth

Flat. There are no drop shadows. The only `box-shadow` values in the code are inset rings, which are edges, not elevation: the 1px Green Edge ring on the active shell tab and the current scoresheet move, and the 1.5px Strong Rule ring that draws the hollow start-position marker on the move rail. Depth is tonal: the shell bar and sunken input wells are the darkest tones, panel tiles sit slightly darker than the Brand Brown ground so they read as recessed, and raised brown carries menus, pickers and tooltips, each with a Strong Rule border where it floats. The only overlay dimming is the promotion chooser over the board (shell at 72%).

### Named Rules
**The No Shadow Rule.** Separation is by tone and 1px rules. If something needs to float, it gets Raised Brown and a Strong Rule border, not a shadow. Inset rings are allowed only as edges: the Green Edge on dark green fills and the rail's start ring.

## Shapes

Small, squared-off corners. Panels, board frames and framed modules are 4px; buttons, inputs and tabs are 3px; the board, scoresheet move chips and the eval bar are 2px. Full rounding is reserved for things that are physically round tokens: tags (10px pill), the engine switch and its knob, colour dots, classification dots and the move-rail markers (circles, 50%, like magnets). Icons are authored strokes on a 20x20 grid, 1.6 stroke, round caps and joins, shown at 18px in the current text colour.

## Components

### Buttons
- **Shape:** gently squared (3px).
- **Default:** Panel Hover fill, Strong Rule border, cream text, 5px 12px, 13.5px. Hover lifts to Raised Brown with a Cream 3 border; active nudges down 1px; disabled at 45% opacity.
- **Primary:** Deep Green fill, cream ink, 1px Green Edge border, 600. Hover goes to select-hover with a Light Green border.
- **Quiet:** transparent, Cream 2 text; hover fills Panel Hover and brightens to Cream. Used for most tool actions.
- **Icon button:** 34x32, transparent, Cream 2 icon; hover fills Panel Hover.
- **Danger:** default button with Hot Red text; hover border Hot Red.
- **Segmented:** joined buttons, the "on" segment filled Deep Green with cream ink.
- **Focus:** 2px Light Green outline, 2px offset, on every focusable element.
- Transitions are 150ms on background, border and colour with `cubic-bezier(.16, 1, .3, 1)`.

### Chips (Tags)
- **Style:** Raised Brown pill with Strong Rule border, 12.5px, a small remove button whose hover fills Hot Red with shell ink.

### Cards / Containers (Panels)
- **Corner Style:** 4px.
- **Background:** Panel Brown, flat.
- **Shadow Strategy:** none (see Elevation).
- **Border:** none on the tile; heads and feet are separated by a 1px Rule.
- **Internal Padding:** head 14px 16px 12px, body 10px 16px 16px, foot 12px 16px.

### Inputs / Fields
- **Style:** Sunken Brown fill, Strong Rule border, 3px, 5px 8px. Hover border Cream 3. Placeholders Cream 3.
- **Focus:** border turns Light Green (no outline ring on text fields).
- **Error:** a 12.5px Hot Red message beneath.

### Navigation
- **Shell bar:** flat Shell Brown, 1px Rule beneath.
- **Shell tabs:** text links in Cream 2, 6px 12px, 3px radius; hover fills Panel Brown; active is filled Deep Green with cream ink at 600 and an inset 1px Green Edge.
- **Panel tabs:** transparent, Cream 2 at 500; active goes Cream at 600 with a 2px Light Green underline inset 8px.

### Scoresheet
Numbered rows on a 40px / 1fr / 1fr grid in the mono. The current move is a 2px-rounded Deep Green chip with cream ink at 700 and an inset 1px Green Edge. Classified moves take their mark colour with a 2px underline offset 4px. Variations indent 48px in 13px, their moves in Mint with Cream 3 parentheses; comments indent the same in the UI font, Cream 2. Above, the player plates: colour dot, name, mono Elo, then event, round, date, ECO and the mono result. Below, the annotation card for the current move.

### Engine Panel
A head with the on/off switch (Mint when on), title, mono depth in Cream 2 and the 26px Mint eval. Lines are ruled rows with a 56px mono score column in Mint and ellipsised moves.

### Evaluation Bar
A 12px flat bar beside the board: shell for Black, cream for White, rising from White's side over 450ms; sunken and empty when the engine is idle.

### Move Rail (Signature)
One round marker per main-line move along a Panel Brown strip (4px radius, 8px 12px padding) under the board, like magnets along the board's edge. The rail is a size container: every marker's diameter comes from the Game's length, `clamp(4px, (100cqi - 24px) / moves - gap, 13px)`, and the gap scales with it (`clamp(1px, 20% of a slot, 4px)`), so a long Game shrinks all markers together and the rail never wraps. Unclassified markers are rail-white / rail-black. Classified markers keep the circle and grow: inaccuracy yellow at 1x, mistake orange at 1.2x, blunder red at 1.4x, each giving the extra width back with negative margins so the row keeps its length. The start position is a hollow ring. The current move is ringed with a 2px cream outline at 2px offset; hover scales a marker to 1.25 over 150ms. Click jumps to the move.

### Board
Chessground with cream and brown squares, 2px radius. Every board sits in a flat Panel Brown frame (4px radius, 14px 14px 30px 40px padding) with mono coordinates outside the squares (15px, 600, Cream 2). On the review screen the frame is the wider board-with-eval-bar panel, which holds the 12px eval bar beside the board, and the inner frame drops its own fill. Last move is a 50% golden tint, selected square a 60% Green Edge tint, move targets are Deep Green dots at 45%, check is a Hot Red radial glow. The engine's best move is one Forest Green arrow (chessground brush `engine`: opacity 0.85, line width 11).

### Annotation Card
The note on the current move sits at the foot of the scoresheet, above a Rule: a 12.5px Cream 2 head naming the move, a comment textarea, and the annotation symbols (mono, 700, 32x26 buttons, "on" filled Deep Green with cream ink) folded behind a quiet "Symbols" toggle whose chevron rotates 180deg over 200ms.

### Classified Moves List
Ruled rows: a 9px mark dot, the mono move (Light Green on hover), the classification in Cream 2, and the engine's best move in Mint. The current row takes a 14% Light Green tint. Each row's small Copy button stays hidden until the row is hovered, focused or current, and is always visible on touch devices.

## Do's and Don'ts

### Do:
- **Do** derive any new colour from the Owner's three: brown #3B2A20, cream #EFE6D8, dark green #173B2C.
- **Do** keep the board the largest element on any screen that shows it, sized from the viewport.
- **Do** use Deep Green (select) only as a fill for current, chosen, active or primary, always with cream ink, and use Light Green (accent) for its thin lines and hover text.
- **Do** use Mint (engine) for anything the engine produced, and nothing else.
- **Do** set every number in Cascadia Mono with tabular figures.
- **Do** separate surfaces with tone and 1px rules; float menus on Raised Brown with a Strong Rule border.
- **Do** draw new icons on the 20x20 grid with a 1.6 round-capped stroke, in currentColor.
- **Do** tile dense modules edge to edge with 1px rule gaps rather than spacing out separate cards.

### Don't:
- **Don't** use drop shadows, gradients or grain on chrome; the only non-flat fills are the board's chessground marks (move dots, check glow), and the only box-shadows are inset edge rings (Green Edge on green fills, the rail's start ring).
- **Don't** use Deep Green for text or thin lines on brown; it does not read. Use Light Green.
- **Don't** introduce a hue outside the three brand colours and the functional marks (engine, good, the classification ramp).
- **Don't** build toolbars or ribbons; tools are quiet icon buttons in one row.
- **Don't** use letter-spaced uppercase labels or small headings above headings.
- **Don't** use icon fonts or emoji glyphs as icons.
- **Don't** show a board without its brown frame and outside coordinates.
