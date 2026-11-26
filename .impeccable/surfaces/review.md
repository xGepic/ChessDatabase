---
version: 1
slug: "review"
primary_target: "review"
related_targets: []
---

# Surface: Game review screen

Scope: the screen for reviewing one Game. It holds the board, the move list with Variations and Annotations, Interactive Analysis, and the Engine Assessment from Game Analysis. The other screens (game list, Opening Tree, Dossier, Training) inherit this world. Mode: Operate.

Audience and job: the Owner, at home at a large monitor, usually in the evening after a game. They step through the Game, read the engine's lines, look at the classifications, and add their own comments.

Constraints: the board is the largest element on screen. The palette is fixed by the Owner: woody browns with cream. It must not feel like ChessBase's grey toolbar clutter.

## Direction contract

THESIS: The screen is the tournament table you just played at. It refuses the generic analysis-site layout of grey panels with a blue accent and puts the Game back on wood: board, scoresheet and engine notes laid on a walnut table.

OWN-WORLD: The ground is a dark walnut (#3b2416), with a faint vertical grain no stronger than 2% contrast. The board has boxwood (#e8cfa0) and rosewood (#a8693d) squares inside a raised wooden frame (#8a5634 to #6a3f24) with a soft cast shadow. Working surfaces are cream cards (#f4ecdc) with ink text (#2a1c12) and small radii. The top bar is a darker walnut (#2c1a0f), and its active tab is a warm wood fill (#7a4a2b). There is one tournament-felt green (#2f5d3a) for engine and live status. Move classifications use amber (inaccuracy), rust (mistake) and deep red (#9a2f1e, blunder). UI text is set in Segoe UI Variable. Moves are set in a monospaced face (Cascadia Mono, then Consolas), so columns line up like a scoresheet.

STORY: The Owner opens a Game and sees the position, who played and the result at a glance. They step through moves on the scoresheet or with the arrow keys while the engine card updates live. They spot the red bars in the Game Analysis strip, jump to that move, and write a comment on it.

FIRST VIEWPORT: There is a 52px top bar with the app name and tabs: Games, Review, Opening Tree, Players, Training. Below it there are three columns. On the left is the board in its frame, as large as the window height allows (about 680–740px at 1440×900). In the middle is a scoresheet card, 360px wide. Its header shows the players with Elo (or "unrated"), the Event, the Round and the result. Below that are numbered rows in two columns, White and Black, with the current move highlighted in boxwood. Variations and comments sit as indented rows spanning both columns under the move they branch from. On the right are stacked cards: the engine card (evaluation, depth, three MultiPV lines, Start/Stop), then the Game Analysis card (the winning-chance-lost bar strip, Accuracy, and Analyse/Copy Assessment). The primary action is the Analyse button on the Game Analysis card.

FORM: Tournament Hall, the wooden club set on a walnut tournament table. This was the IMPECCABLE’S PICK card (my first-ranked grounded candidate), chosen over the assigned Club Yearbook. seed:2d3e6bbdb2c4b9cc. Signature move: the notation is a real scoresheet, with numbered rows and White and Black columns, and the move you're on is the highlighted row.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Open decisions

- Whether long evening sessions need a lighter walnut ground. Check this once the screen is built; it was named as the direction's risk.
- The piece set (chessground's default, or a classic Staunton set) is decided at build time.
