# Tickets: Chess Database

Built from [the spec](../specs/0001-chess-database.md). Tickets are numbered so that each one's blockers come before it. Start with any ticket whose blockers are all done.

Every ticket is only done once README.md reflects it. If the ticket changes how to build, run, test or use the app, update the README in the same change.

## Overview

| # | Ticket | Blocked by | Status |
|---|---|---|---|
| 01 | [Walking skeleton](#01-walking-skeleton) | – | **Done** |
| 02 | [Chess Rules core](#02-chess-rules-core) | 01 | **Done** |
| 03 | [Enter a Game and see it in the game list](#03-enter-a-game-and-see-it-in-the-game-list) | 01, 02 | **Done** |
| 04 | [Replay a Game and branch into Variations](#04-replay-a-game-and-branch-into-variations) | 03 | **Done** |
| 05 | [Annotate moves](#05-annotate-moves) | 04 | **Done** |
| 06 | [Game details, Tags and ECO](#06-game-details-tags-and-eco) | 03 | **Done** |
| 07 | [Import PGN](#07-import-pgn) | 05, 06 | **Done** |
| 08 | [Import review list](#08-import-review-list) | 07 | **Done** |
| 09 | [Export to PGN](#09-export-to-pgn) | 05, 06 | **Done** |
| 10 | [Filter and sort the game list](#10-filter-and-sort-the-game-list) | 06, 09 | **Done** |
| 11 | [Interactive Analysis with Stockfish](#11-interactive-analysis-with-stockfish) | 04 | **Done** |
| 12 | [Game Analysis of a single Game](#12-game-analysis-of-a-single-game) | 11 | **Done** |
| 13 | [Game Analysis queue, re-run and analysed indicator](#13-game-analysis-queue-re-run-and-analysed-indicator) | 12 | **Done** |
| 14 | [Copy Engine Assessment into the Game](#14-copy-engine-assessment-into-the-game) | 05, 12 | **Done** |
| 15 | [Daily backups](#15-daily-backups) | 01 | **Done** |
| 16 | [Opening Tree from my Games](#16-opening-tree-from-my-games) | 04 | **Done** |
| 17 | [Position Search](#17-position-search) | 16 | **Done** |
| 18 | [Masters source in the Opening Tree](#18-masters-source-in-the-opening-tree) | 16 | **Done** |
| 19 | [Tablebase lookup](#19-tablebase-lookup) | 04 | **Done** |
| 20 | [Opponent Dossier](#20-opponent-dossier) | 06 | **Done** |
| 21 | [Merge Players](#21-merge-players) | 20 | **Done** |
| 22 | [Owner Dossier and statistics](#22-owner-dossier-and-statistics) | 12, 20 | **Done** |
| 23 | [Repertoire builder](#23-repertoire-builder) | 16 | **Done** |
| 24 | [Deviation check](#24-deviation-check) | 23 | **Done** |
| 25 | [Repertoire Training](#25-repertoire-training) | 23 | **Done** |
| 26 | [Mistake Training](#26-mistake-training) | 12, 25 | **Done** |
| 27 | [Copy diagram as image](#27-copy-diagram-as-image) | 04 | **Done** |
| 28 | [PDF export of an annotated Game](#28-pdf-export-of-an-annotated-game) | 05, 27 | **Done** |
| 29 | [Leela Chess Zero and engine selection](#29-leela-chess-zero-and-engine-selection) | 13 | **Done** |

When a ticket is finished, set its Status here to **Done**, set the **Status:** line in the ticket itself to `done`, and tick its acceptance criteria.

---

## 01: Walking skeleton

**What to build:** The app launches as a desktop window on Windows and shows an empty game list loaded from the backend, end to end. The structure is set up so every later ticket only adds behaviour: an ASP.NET Core (.NET 11) backend, an Angular UI hosted in a desktop window (Photino.NET or WebView2) per ADR 0001, a SQLite Collection through EF Core with migrations, an API integration test project using the in-process test host with a temporary SQLite file per test, and a Playwright smoke project. The app also runs as a localhost web app for development.

**Blocked by:** None (can start immediately).

**Status:** done

- [x] Starting the app opens a desktop window showing an (empty) game list fetched from the API
- [x] The same UI runs in a normal browser against the backend in development mode
- [x] The SQLite database is created and migrated automatically on first start in a per-user app data folder
- [x] An API integration test calls the game list endpoint against a fresh temporary database and gets an empty list
- [x] A Playwright smoke test opens the app and sees the empty game list
- [x] One command builds and runs all tests
- [x] README.md exists with what the app is, prerequisites, how to run it (desktop and browser), how to run the tests, the project layout, and links to the glossary, spec, ADRs and tickets

---

## 02: Chess Rules core

**What to build:** The C# Chess Rules module, which ADR 0002 makes the single source of truth: board representation, legal move generation, check, checkmate and stalemate detection, FEN read and write, SAN parse and format, and Zobrist position hashing. The SAN parser accepts German piece letters (K D T L S) and always outputs English letters (K Q R B N). It may be written in-house or come from a vetted library wrapped behind our own interface. This is the one module tested directly instead of through the API.

**Blocked by:** 01 (Walking skeleton).

**Status:** done

- [x] Perft results match the published values for the standard positions (initial, "Kiwipete" and the other standard perft positions) at the usual test depths
- [x] SAN round trip: every legal move in the perft positions formats to SAN and parses back to the same move, with correct disambiguation, check and mate suffixes
- [x] German SAN input (e.g. `Sf3`, `Dxd8+`, `Lb5`, `Txe1`) parses to the same move as the English equivalent
- [x] FEN round trip for the standard test positions
- [x] Positions reached by different move orders have equal hashes; differing side to move, castling rights or en passant give different hashes

---

## 03: Enter a Game and see it in the game list

**What to build:** The Owner sets their name in Settings, creates a new Game by choosing their colour and an Opponent (autocompleted from existing Players, or created inline), enters both Elos (empty or 0 means unrated), plays the main-line moves on a chessground board, and sets the result. Each change saves immediately. The Game then appears in the game list with date, Opponent, colour, Elos and result. The backend rejects illegal moves and Games without a result. The UI uses chess.js only for instant feedback.

**Blocked by:** 01 (Walking skeleton), 02 (Chess Rules core).

**Status:** done

- [x] The Owner's name can be set in Settings and is shown as the Owner's side of every Game
- [x] Opponent autocomplete suggests existing Players; a new Player can be created inline
- [x] Elo empty or 0 is stored and shown as "unrated" for either side, including the Owner's
- [x] Moves are entered by drag or click; illegal moves are rejected instantly in the UI and refused by the API
- [x] The result (1-0, 0-1, ½-½) is required; the API refuses to save a Game as complete without one
- [x] Changes save automatically without a Save button; reopening the app shows the Game unchanged
- [x] A Game can be deleted after confirmation
- [x] The board is shown from the Owner's colour by default
- [x] API tests cover creating a Game, adding moves, refusing an illegal move, Elo 0 reading back as unrated, and refusing a missing result
- [x] The Playwright smoke test enters a short Game and finds it in the game list

---

## 04: Replay a Game and branch into Variations

**What to build:** The Owner opens a Game and steps through it with arrow keys or by clicking moves in the notation, and can flip the board. Playing a move that differs from the recorded continuation creates a Variation automatically, with no dialog. Variations can nest. Right-clicking a move offers: promote to main line, delete Variation, delete from here. The notation panel renders the main line and nested Variations readably. The move tree is stored as nodes, and every tree operation is validated by Chess Rules.

**Blocked by:** 03 (Enter a Game and see it in the game list).

**Status:** done

- [x] Arrow keys (back, forward, start, end) and clicking in the notation navigate the Game
- [x] Playing a different move at any position creates a new Variation and leaves the existing line intact
- [x] Variations within Variations work to any depth and render nested in the notation
- [x] Promote to main line, delete Variation and delete from here work and save immediately
- [x] Promoting a Variation makes it the main line used everywhere else (result unchanged)
- [x] API tests cover adding a differing move (a Variation appears), nesting, promotion and both deletes

---

## 05: Annotate moves

**What to build:** The Owner adds a text comment to any move (main line or Variation) and attaches symbols (NAGs) such as `!`, `?`, `!!`, `??`, `!?`, `?!` and position symbols such as `=`, `±`, `∓`, `+-`, `-+`. Comments and symbols render inline in the notation, so an annotated Game reads like a book. Comment edits are debounced in the UI and saved automatically.

**Blocked by:** 04 (Replay a Game and branch into Variations).

**Status:** done

- [x] A comment can be added, edited and removed on any move, including moves in Variations
- [x] Move symbols and position symbols can be set and cleared from a quick picker
- [x] Comments and symbols render inline in the notation panel
- [x] Comment typing is debounced and persists without a Save button
- [x] API tests cover setting and reading back comments and NAGs on main-line and Variation moves

---

## 06: Game details, Tags and ECO

**What to build:** The Owner fills in optional Game details: Event, Site, Date (partial dates such as "2024" or "2024-03" allowed), Round, time control and private notes, and attaches free-form Tags. Event and Site autocomplete from earlier Games. The ECO code and opening name are derived automatically from the main line using the bundled Lichess chess-openings dataset (CC0), picking the deepest matching position, and update when the main line changes. The game list shows Event and ECO.

**Blocked by:** 03 (Enter a Game and see it in the game list).

**Status:** done

- [x] All listed details are optional and editable in the Game's details panel
- [x] Event and Site autocomplete from values used before; choosing an existing Event links the Game to it
- [x] Tags can be added and removed, and autocomplete from existing Tags
- [x] ECO code and opening name are filled in automatically and update when the main line changes (e.g. after promoting a Variation)
- [x] The game list shows Event and ECO columns
- [x] API tests cover partial dates, Tag assignment, Event autocomplete and ECO derivation for a few known openings

---

## 07: Import PGN

**What to build:** The Owner imports a PGN file with many Games, or pastes PGN text. Variations, comments and NAGs are kept. German piece letters are converted to English. In Settings the Owner lists Aliases. A Game where one side matches the Owner's name or an Alias is assigned to the Owner automatically, and the other side becomes the Opponent (an existing Player if the name matches exactly, otherwise a new Player). A missing or 0 Elo is imported as unrated. Event, Site, Date, Round and time control map to Game details. One malformed Game never aborts the import. Afterwards, a summary shows how many Games were imported, how many need review and how many failed, each with a reason. In this ticket, Games needing review (no Owner match, missing result) are only counted and listed. Resolving them is ticket 08.

**Blocked by:** 05 (Annotate moves), 06 (Game details, Tags and ECO).

**Status:** done

- [x] PGN file import and PGN paste both work for multi-game input
- [x] Nested Variations, comments and NAGs survive import and render in the notation
- [x] German-letter moves import correctly and display in English
- [x] Aliases can be managed in Settings; matching is case-insensitive and tolerant of "Surname, Firstname" vs "Surname F." forms of the Owner's own name (Aliases later removed: the usual name forms are enough)
- [x] Missing or 0 Elo tags import as unrated
- [x] A malformed Game is reported in the summary with a reason; the other Games still import
- [x] Games with no Owner match or no result are held back from the Collection and counted as "needs review"
- [x] API tests import a fixture PGN containing all of the above cases and check the summary and the resulting Games

---

## 08: Import review list

**What to build:** A review list where the Owner resolves imported Games that need attention: picking which side was the Owner when no name matched, and setting the result when it was missing (`*`). During import, Opponent names that closely match an existing Player (e.g. "Huber T." vs "Huber, Thomas") are suggested as the same person. The Owner confirms or rejects each suggestion, and Players are never merged without confirmation. Games that duplicate an existing Game (same Players, date and moves) are flagged and skipped unless the Owner chooses to import them anyway. The import summary now includes duplicates.

**Blocked by:** 07 (Import PGN).

**Status:** done

- [x] Held-back Games appear in a review list; choosing the Owner's side and/or a result moves them into the Collection
- [x] Close Opponent name matches are suggested with the existing Player shown; confirm links to that Player, reject creates a new Player
- [x] No Player is ever linked to a non-identical name without the Owner's confirmation
- [x] Importing the same file twice flags every Game as a duplicate and adds nothing by default
- [x] The summary reports imported, needs review, duplicates and failed
- [x] API tests cover each resolution path and duplicate detection

---

## 09: Export to PGN

**What to build:** The Owner exports a single Game, or the whole Collection, to a PGN file. The output is standard PGN with English piece letters, including the Seven Tag Roster, Elo tags (unrated sides omit the Elo tag), the other Game details, nested Variations, comments and NAGs. The Engine Assessment is not exported unless it has been copied into the Game.

**Blocked by:** 05 (Annotate moves), 06 (Game details, Tags and ECO).

**Status:** done

- [x] Export single Game and export entire Collection are available from the UI
- [x] Exported PGN re-imports into an empty Collection with identical moves, Variations, comments, NAGs and details (round-trip test)
- [x] Unrated sides produce no Elo tag; partial dates use PGN's `??` notation
- [x] The output opens in another standard PGN tool (manually verified once with a sample file)

---

## 10: Filter and sort the game list

**What to build:** The Owner sorts the game list by any column and filters by Opponent, Event, date range, result, Owner's colour, ECO range, Tag and time control. Filters combine (e.g. "my losses with Black in the club league in 2025"). The filtered list can be exported to PGN. Clicking a row opens the Game.

**Blocked by:** 06 (Game details, Tags and ECO), 09 (Export to PGN).

**Status:** done

- [x] Every column is sortable both ways
- [x] Each filter works alone and in combination with the others
- [x] Result filter can be expressed from the Owner's point of view (win, draw, loss)
- [x] "Export filtered list to PGN" exports exactly the Games shown
- [x] API tests cover combined filters against a seeded Collection

---

## 11: Interactive Analysis with Stockfish

**What to build:** The Owner switches the Engine on while viewing any position and sees up to 3 best lines with evaluation and depth, updating live, plus an evaluation bar beside the board. The Engine follows the Owner as they move through the Game, and stops when switched off or when the Game is closed. Clicking an Engine line plays it into the Game as a Variation. Settings hold the Stockfish executable path and the Interactive Analysis options (defaults: 16 threads, 8 GB hash, MultiPV 3, no time limit). The Engine Host manages UCI processes and streams output to the UI over SignalR. From the start it supports several engine definitions, though only Stockfish is configured. This ticket also builds the scripted fake UCI engine used for deterministic tests.

**Blocked by:** 04 (Replay a Game and branch into Variations).

**Status:** done

- [x] Stockfish path and options are configurable in Settings with the stated defaults; a missing or invalid path gives a clear message
- [x] Turning the Engine on streams up to 3 lines (evaluation, depth, moves in SAN) and updates the evaluation bar
- [x] Moving to another position restarts analysis there without manual action
- [x] Turning the Engine off or leaving the Game ends the search; no orphaned Stockfish processes remain after the app closes
- [x] Clicking a line adds it as a Variation at the current position
- [x] A fake UCI engine program exists that replies with canned output per position; hub tests use it to verify streaming, follow and stop behaviour
- [x] An optional slow test category runs real Stockfish at low depth as a smoke check

---

## 12: Game Analysis of a single Game

**What to build:** The Owner starts Game Analysis on the open Game and keeps working while it runs in the background with visible progress. The Engine evaluates every main-line position (defaults: 2 seconds per position, MultiPV 1, Interactive Analysis thread and hash settings). The Assessment module converts evaluations to winning chance (Lichess formula), classifies each move by the winning chance it loses (inaccuracy ≥ 10, mistake ≥ 20, blunder ≥ 30 points on Lichess's −100…+100 winning-chance scale, as Lichess classifies; that is 5, 10 and 15 percentage points of winning chance), finds the best move and line, and computes Accuracy per side (Lichess method). The result is saved as the Game's Engine Assessment, separate from the Owner's Annotations (ADR 0003). The Game view shows an evaluation graph (click a point to jump there), coloured markers on classified moves, the best move for each classified move, and both Accuracy figures.

**Blocked by:** 11 (Interactive Analysis with Stockfish).

**Status:** done

- [x] Game Analysis runs in the background with progress shown; the UI stays usable
- [x] The Engine Assessment is saved and shown again on reopening, without re-analysis
- [x] The evaluation graph covers the whole Game and is clickable
- [x] Classified moves show their marker and the Engine's best move
- [x] Accuracy is shown for both sides
- [x] The move tree and the Owner's Annotations are unchanged by analysis
- [x] Assessment tests with fixed evaluations verify winning-chance conversion (including mate scores), each classification threshold, a large swing inside an already-won position not being a blunder, and Accuracy values
- [x] An end-to-end API test with the fake UCI engine produces an exact, expected Engine Assessment

---

## 13: Game Analysis queue, re-run and analysed indicator

**What to build:** The Owner selects several Games in the game list (e.g. a whole tournament) and queues them for Game Analysis. They run one after another with overall progress, and the queue keeps going while the Owner works elsewhere in the app. The game list shows which Games have an Engine Assessment. The Owner can re-run Game Analysis with different settings, which replaces the previous Engine Assessment and records the engine and settings used.

**Blocked by:** 12 (Game Analysis of a single Game).

**Status:** done

- [x] Multiple Games can be queued from the game list; overall and per-Game progress are visible
- [x] The queue can be cancelled; completed Engine Assessments are kept
- [x] The game list has an "analysed" indicator and can be filtered by it
- [x] Re-running replaces the old Engine Assessment; the engine name, version and settings used are visible
- [x] API tests with the fake engine cover queue order, cancel and replace

---

## 14: Copy Engine Assessment into the Game

**What to build:** From the Engine Assessment, the Owner copies chosen parts into the Game as real Annotations: the classification symbol (`?!`, `?`, `??`) on a move, and the Engine's best line as a Variation. This works for a single move or for "all mistakes and blunders" at once. Copying is the only route from Engine Assessment to Annotations (ADR 0003). It never overwrites an Owner comment or symbol that is already there.

**Blocked by:** 05 (Annotate moves), 12 (Game Analysis of a single Game).

**Status:** done

- [x] Per-move "copy to Game" adds the symbol and the best line as a Variation
- [x] "Copy all mistakes and blunders" does the same for every qualifying move (later removed: too easy to flood a Game with Variations)
- [x] An existing Owner symbol or comment on that move is never replaced
- [x] Copying the same move twice doesn't create a duplicate Variation
- [x] API tests cover single copy, bulk copy and the no-overwrite rule

---

## 15: Daily backups

**What to build:** Once a day (on first start that day, and then every 24 hours while running), the app copies the Collection's SQLite database to a configurable backup folder, using SQLite's online backup so the copy is consistent while the app runs. Only the 10 most recent backups are kept. Settings show the backup folder and the time of the last backup, and offer a "Back up now" button.

**Blocked by:** 01 (Walking skeleton).

**Status:** done

- [x] A backup is created on the first start of each day and every 24 hours while running
- [x] Only the 10 newest backups remain; older ones are deleted
- [x] Backup folder is configurable; the last backup time is shown; "Back up now" works
- [x] A backup file opens as a valid database containing the same Games
- [x] Tests cover rotation (with a controllable clock) and the integrity of the copy

---

## 16: Opening Tree from my Games

**What to build:** A position index of every main-line position in each Game (Variations excluded), rebuilt whenever a Game's main line changes and filled for all existing Games on migration. The Game view gets an Opening Tree panel showing, for the current position, every move played from it in the Owner's Games, with the number of Games and the Owner's score. Clicking a move plays it on the board. The panel also lists the Games that reached the current position, and opening one jumps to that position.

**Blocked by:** 04 (Replay a Game and branch into Variations).

**Status:** done

- [x] The Opening Tree shows moves, Game counts and the Owner's score for the current position
- [x] Positions reached by different move orders are counted together
- [x] Clicking a move plays it; the Game list for the position opens Games at that position
- [x] Promoting a Variation or editing the main line updates the tree immediately
- [x] Variation moves never appear in the tree
- [x] API tests over a seeded Collection cover counts, scores and transpositions

---

## 17: Position Search

**What to build:** The Owner reaches a position (on the board in any Game or analysis board) or sets one up with a position editor (place pieces, side to move, castling rights), then searches for every Game where it occurred in the main line. Each result opens the Game at the matching position.

**Blocked by:** 16 (Opening Tree from my Games).

**Status:** done

- [x] Search from the current board position works from any Game view
- [x] A position editor lets the Owner set up an arbitrary legal position and search it
- [x] Results list each Game with the move number where the position occurred; opening a result jumps there
- [x] Positions that occur only in Variations are not found
- [x] API tests cover a transposition hit and a Variation-only miss

---

## 18: Masters source in the Opening Tree

**What to build:** The Opening Tree gets a "Masters" tab that shows, for the current position, move statistics from Master Games via the Lichess masters opening explorer API, plus the top Master Games for the position. A top game can be viewed read-only on the board. It is never stored in the Collection. Before implementing, check the API's current terms and authentication requirements. If it needs a personal token, add it to Settings. When offline or unavailable, the tab says so and nothing else is affected.

**Blocked by:** 16 (Opening Tree from my Games).

**Status:** done

- [x] The Masters tab shows moves with game counts and White/draw/Black percentages for the current position
- [x] The top Master Games are listed and viewable read-only; they are not added to the Collection
- [x] Offline or API errors show "Masters unavailable" within a short timeout; the rest of the app keeps working
- [x] Results are cached for the session to avoid repeat calls
- [x] Tests use a fake HTTP handler for success, timeout and error responses

---

## 19: Tablebase lookup

**What to build:** When the current position has 7 or fewer pieces, the Game view shows the exact Tablebase result (win, draw or loss, with distance to mate or zeroing where available) and the best moves, using the Lichess tablebase API. Lookups fail quietly when offline.

**Blocked by:** 04 (Replay a Game and branch into Variations).

**Status:** done

- [x] Positions with 7 or fewer pieces show the Tablebase verdict and a ranked move list
- [x] Positions with more pieces show nothing and make no call
- [x] Offline or API errors show a subtle "Tablebase unavailable" and nothing breaks
- [x] Tests use a fake HTTP handler for success, timeout and error responses

---

## 20: Opponent Dossier

**What to build:** A Dossier page for each Player: the Owner's head-to-head score (wins, draws, losses), the Player's rating history charted from the Elo recorded in the Owner's Games (unrated Games left out), the openings they played against the Owner (by ECO, with results), and the list of Games against them. The Owner can record an optional FIDE ID and club. The Dossier is reachable from the game list, the Game view and a Players list.

**Blocked by:** 06 (Game details, Tags and ECO).

**Status:** done

- [x] A Players list with search links to each Dossier
- [x] Head-to-head score, rating history chart, openings and the Games list are correct for a seeded Collection
- [x] Unrated Games are left out of the rating history
- [x] FIDE ID and club can be edited
- [x] API tests cover the Dossier figures

---

## 21: Merge Players

**What to build:** From a Dossier or the Players list, the Owner merges two Player records that are really the same person, choosing which name to keep. All Games of the merged Player move to the kept Player, and the Dossier figures combine.

**Blocked by:** 20 (Opponent Dossier).

**Status:** done

- [x] Merging asks for confirmation and which name, FIDE ID and club to keep
- [x] All Games are re-pointed; the merged Player disappears; Dossier figures reflect the union
- [x] API tests cover the merge

---

## 22: Owner Dossier and statistics

**What to build:** The Owner's own Dossier: overall score, score by colour, score by opening (ECO) and colour, own rating history charted from the Owner's Elo in each Game, average opponent rating (unrated Opponents left out), and Accuracy over time across Games that have an Engine Assessment. All views can be limited by date range and time control.

**Blocked by:** 12 (Game Analysis of a single Game), 20 (Opponent Dossier).

**Status:** done

- [x] Scores by colour and by opening are correct for a seeded Collection
- [x] The rating history and average opponent rating leave out unrated values
- [x] The Accuracy-over-time chart includes only analysed Games
- [x] Date range and time control filters apply to every figure (time control filter later removed: the Owner plays one time control)
- [x] API tests cover the figures, including the unrated exclusions

---

## 23: Repertoire builder

**What to build:** The Owner builds a Repertoire for White and one for Black as move trees in a dedicated editor (board plus tree, with the same Variation behaviour as Games). Lines can be added by playing moves, from a Game ("add this line up to here to my Repertoire"), or from the Opening Tree panel. Repertoire lines can carry comments.

**Blocked by:** 16 (Opening Tree from my Games).

**Status:** done

- [x] Separate White and Black Repertoires can be edited as trees; edits save automatically
- [x] "Add to Repertoire" from a Game position and from the Opening Tree adds the line, merging it with existing branches
- [x] Lines can be deleted, and comments added
- [x] API tests cover adding, merging and deleting lines

---

## 24: Deviation check

**What to build:** For each Game, the app finds the Deviation: the first main-line move where either side left the Owner's Repertoire for the Owner's colour. It shows who deviated (the Owner or the Opponent) and marks the move in the Game view. A list shows the Games where the Owner left their own Repertoire, so the lines the Owner keeps forgetting can be found. The check works by position, so transpositions back into the Repertoire count as in-book.

**Blocked by:** 23 (Repertoire builder).

**Status:** done

- [x] The Game view marks the Deviation move and says who deviated
- [x] Games that never left the Repertoire before it ran out are shown as "end of Repertoire" rather than a Deviation
- [x] A list of the Owner's own Deviations, with the expected Repertoire move, is available and filterable
- [x] API tests cover Owner Deviation, Opponent Deviation, end of Repertoire and a transposition

---

## 25: Repertoire Training

**What to build:** A training session that quizzes the Owner on their Repertoire. The app plays the Opponent's moves, and the Owner must play the Repertoire move for their colour. Each Repertoire position the Owner must answer is a training item scheduled with spaced repetition (SM-2 style): right answers push the next review further out, and wrong answers bring it back soon and show the correct move. The scheduler is a shared, reusable component for ticket 26.

**Blocked by:** 23 (Repertoire builder).

**Status:** done

- [x] A session presents due items and checks the Owner's move against the Repertoire
- [x] A wrong answer shows the correct move and schedules the item sooner; a right answer schedules it later
- [x] The number of items due today is shown; new Repertoire lines become new items automatically
- [x] Scheduler tests with a controllable clock verify the interval progression and reset on failure

---

## 26: Mistake Training

**What to build:** Every move classified as a mistake or blunder in an Engine Assessment becomes a Mistake Training item: the position before the Owner's move, where the Owner must find a better move. Any move within a small winning-chance margin of the Engine's best counts as correct. Items are scheduled with the spaced-repetition scheduler from ticket 25. From any item, the Owner can open the source Game at that position. Only the Owner's own mistakes are used, not the Opponent's.

**Blocked by:** 12 (Game Analysis of a single Game), 25 (Repertoire Training).

**Status:** done

- [x] Analysed Games automatically yield items for the Owner's mistakes and blunders only
- [x] Answers within the margin of the best move count as correct; the Engine's best line is shown afterwards
- [x] Items follow spaced repetition; re-running Game Analysis updates the Game's items without losing progress on unchanged positions
- [x] "Open source Game" jumps to the position in context
- [x] API tests with the fake engine cover item creation, answer checking and the regeneration rule

---

## 27: Copy diagram as image

**What to build:** From the Game view, the Owner copies the current position to the clipboard as a PNG board diagram (oriented from the Owner's colour, optional coordinates and last-move highlight), ready to paste into other programs. The diagram renderer is a reusable backend component for PDF export (ticket 28).

**Blocked by:** 04 (Replay a Game and branch into Variations).

**Status:** done

- [x] "Copy diagram" puts a PNG of the current position on the clipboard
- [x] Orientation follows the board's current orientation; coordinates and last-move highlight are toggleable
- [x] API tests render known positions and check image dimensions and that rendering is deterministic (stable output for the same input)

---

## 28: PDF export of an annotated Game

**What to build:** The Owner exports a Game to PDF: a header with the Players, Elos, Event, date and result, then the full notation with Variations, comments and symbols, and board diagrams at the moves the Owner marks for a diagram (plus optionally the final position). Ready to print or send to a coach.

**Blocked by:** 05 (Annotate moves), 27 (Copy diagram as image).

**Status:** done

- [x] The Owner can mark moves "diagram here" in the Game view; the marks are saved with the Game
- [x] The PDF contains the header, notation with nested Variations, comments and symbols, and diagrams at the marked moves
- [x] Unrated sides print as "unrated"
- [x] A test generates a PDF for a fixture Game and checks that its text includes the expected header and moves

---

## 29: Leela Chess Zero and engine selection

**What to build:** The Owner adds Leela Chess Zero (Lc0) as a second engine in Settings: the executable path, a network weights file and the GPU backend options. For Interactive Analysis and for Game Analysis separately, the Owner chooses which configured engine to use. Engine Assessments record which engine produced them.

**Blocked by:** 13 (Game Analysis queue, re-run and analysed indicator).

**Status:** done

- [x] Multiple engine definitions can be added, edited and removed in Settings, each with its own UCI options
- [x] The engine for Interactive Analysis and for Game Analysis is chosen separately
- [x] Lc0 runs on the GPU with the configured weights and streams lines like Stockfish
- [x] The Engine Assessment shows which engine produced it
- [x] Engine Host tests with two fake engines verify that selection routes to the right process

Note: Lc0 was not installed when this was built, so it was tested against the fake engine announcing Lc0's options (Threads, MultiPV, WeightsFile, Backend; no Hash). Check it once with the real Lc0 and a network file.
