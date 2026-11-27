---
version: 1
slug: "ui-src-app-review-game-review-ts"
primary_target: "ui/src/app/review/game-review.ts"
related_targets: ["ui/src/styles.css","ui/src/app/app.html"]
---

# Surface: Game review screen (and the app shell it sets)

Scope: the screen for reviewing one Game: board, scoresheet with Variations and Annotations, Interactive Analysis, and the Engine Assessment from Game Analysis. It sets the world for the app shell and every other screen (Games, Players and Dossier, My statistics, Position Search, Repertoire, Training, Import, Settings), which inherit it. Mode: Operate.

Audience and job: the Owner, at home at a large monitor in a dim room, usually in the evening after a game, using all screens about equally. They step through the Game, read the engine's lines, look at the classifications, and add their own comments.

Constraints: the board is the largest element on the review screen. Dark. It must not feel like ChessBase's grey toolbar clutter. 2026-11-27 redesign: the Owner dropped the walnut-and-cream "Tournament Hall" world (look, layouts and polish all unsatisfying); the earlier woody-palette pin no longer binds.

## Direction contract

THESIS: Your game hung on the wall of the club's analysis room: a big, flat demonstration board on slate, talked through and marked up after the round. It refuses both the grey analysis-site panels and ChessBase's toolbar clutter.

OWN-WORLD: 2026-11-27, palette pinned by the Owner as three colours: brown #3B2A20, cream #EFE6D8, dark green #173B2C. Ground #3b2a20 with darker brown panels #2d2017, shell bar #22170f, rules #4a3829, 4px radii, flat colour, no grain, no shadows. Board: cream #efe6d8 and brown #9a7457 squares in a brown frame with large mono coordinates. Text cream #efe6d8, secondary #cbbda8. The pinned green #173b2c fills selection, the active tab, the current move and primary actions (cream text, a #2d6b51 edge so it reads on brown); a lighter green of the same hue #6fb592 for focus rings, tab underlines and hover text; engine #8cc7a8 (board arrow #1f6b4a). Inaccuracy #ecc65a, mistake #e38a4c, blunder #e85a3f. Segoe UI Variable for UI; every number in Cascadia Mono, tabular. Raises: Statistics and Dossier tile ruled modules edge to edge; opponent identity plate and struck-through lines open with the Owner.

STORY: The Owner opens a Game and sees the position, who played and the result at a glance. They step through with arrow keys or the scoresheet while the engine panel updates live, spot the red marker on the magnet rail, jump to it, and write a comment.

FIRST VIEWPORT: 48px shell bar: app name, then tabs Games, Players, My statistics, Position Search, Repertoire, Training, Import, Settings; active tab filled chalk yellow. Three columns: board in its slate frame at full height (~640–700px at 1440×900) with the magnet rail beneath; a 340px scoresheet panel headed by both players' plates, event, round, date, ECO, result, then numbered White/Black rows with the current row filled chalk yellow and variations/comments indented beneath; a right column with the Engine panel (big chalk-blue eval, depth, three lines, Start/Stop) over the Game Analysis panel (eval plot, accuracy, Analyse primary, Copy Assessment).

FORM: Analysis Room, the club's wall demonstration board. Candidate 7 of my ordered list, dealt by the roll and chosen over Informator (pick) and Catalogue Sleeve. seed:1bcad175c1ccb961. Signature move: the magnet rail, one round marker per move under the board coloured by classification, the current move ringed; click to jump.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Open decisions

- Piece set: chessground's default cburnett or a flatter set that suits the demo-board world; decide at build time.
- Whether the green board risks reading as chess.com; check against the built screen.
