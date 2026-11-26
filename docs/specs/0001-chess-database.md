---
status: ready-for-agent
---

# Spec: Personal chess database with engine analysis

Vocabulary follows [GLOSSARY.md](../../GLOSSARY.md). Architecture follows [ADR 0001](../adr/0001-aspnet-core-angular-desktop-shell.md), [ADR 0002](../adr/0002-csharp-is-source-of-truth-for-chess-rules.md) and [ADR 0003](../adr/0003-engine-assessment-separate-from-annotations.md).

## Problem Statement

The Owner is a hobby over-the-board chess player with about 100 annotated Games on paper and in PGN files. The Games have nowhere to live: they can't be searched by Opponent, Event or opening, and there's no easy way to replay them with a strong Engine alongside, to see which moves were mistakes, to find out how the Owner usually does in a given line, or to learn from those mistakes. Commercial tools like ChessBase do this but are expensive and built around huge reference databases the Owner doesn't need. The Owner has a powerful Windows PC (i9-13900K, 64 GB RAM, RTX 4090) that could run a top Engine at full strength.

## Solution

A personal Windows desktop app that holds the Owner's Collection of Games in a local database. The Owner types Games in on a board or imports them from PGN. Each Game records both Players, their Elo (or unrated), the result, optional details, the moves with Variations, and the Owner's own Annotations. The Owner replays any Game with Stockfish running live alongside (Interactive Analysis), or has the Engine go through the whole Game unattended (Game Analysis). Game Analysis produces an Engine Assessment with an evaluation graph, Move Classifications and Accuracy, without touching the Owner's Annotations.

Later phases add research (Opening Tree over the Owner's Games plus online Master Games, Position Search, online Tablebase lookups), Player Dossiers, an opening Repertoire with training, Mistake Training from the Owner's own errors, export to PDF, images and PGN, and Leela Chess Zero as a second Engine on the GPU.

Delivery happens in five phases. Each phase leaves the app usable:

1. **Core**: entering and importing Games, board and notation, the game list and search, Interactive Analysis, Game Analysis.
2. **Research**: Opening Tree (My games and Masters), Position Search, Tablebase.
3. **People**: Dossiers and the Owner's statistics.
4. **Training**: Repertoire, Deviation check, Repertoire Training, Mistake Training.
5. **Output**: PDF and diagram export, Lc0 support.

## User Stories

### Setup and settings (phase 1)

1. As the Owner, I want to enter my name once in Settings, so that I never have to type it when entering a Game.
2. As the Owner, I want to list Aliases (other spellings of my name), so that imported Games with my name spelled differently are still recognised as mine.
3. As the Owner, I want to set where the Stockfish executable lives, so that I can update Stockfish independently of the app.
4. As the Owner, I want to adjust Engine threads, hash size, number of lines and Game Analysis time per move, so that I can trade analysis strength against PC load.
5. As the Owner, I want sensible defaults (16 threads, 8 GB hash, 3 lines, 2 seconds per move), so that analysis is strong without slowing my PC to a crawl.
6. As the Owner, I want to choose the backup folder, so that backups can go to a different drive.

### Entering a Game (phase 1)

7. As the Owner, I want to create a new Game by choosing my colour and my Opponent, so that every Game is anchored to me from the start.
8. As the Owner, I want Opponent names to autocomplete from existing Players, so that the same person is never recorded twice under different spellings.
9. As the Owner, I want to create a new Player inline when the Opponent is new, so that entering a Game doesn't require a detour.
10. As the Owner, I want to enter both Elos, so that I know the rating situation of each Game.
11. As the Owner, I want an Elo left empty or entered as 0 to mean unrated, so that I can record Games against unrated Players, or from before I had a rating.
12. As the Owner, I want unrated sides displayed as "unrated", so that it's clearly different from a missing value.
13. As the Owner, I want to enter Event, Site, Date, Round and time control optionally, so that I can record what I know without being blocked by what I don't.
14. As the Owner, I want Event and Site to autocomplete from previous Games, so that a tournament's Games group together reliably.
15. As the Owner, I want to attach Tags to a Game, so that I can group Games my own way (e.g. "club league", "blitz").
16. As the Owner, I want private notes on a Game, so that I can record context that isn't move commentary.
17. As the Owner, I want to enter moves by dragging or clicking pieces on the board, so that typing in a scoresheet is quick.
18. As the Owner, I want illegal moves to be rejected instantly, so that I can't record an impossible Game.
19. As the Owner, I want to set the result, so that the Game is complete.
20. As the Owner, I want the app to refuse a Game without a result, so that no Game ends up permanently half-recorded.
21. As the Owner, I want the ECO code filled in automatically from the moves, so that I never have to look it up.
22. As the Owner, I want every change saved automatically, so that I never lose work by forgetting to press Save.
23. As the Owner, I want to delete a Game after confirming, so that I can remove mistakes.

### Variations and Annotations (phase 1)

24. As the Owner, I want playing a different move from the recorded one to start a new Variation automatically, so that exploring ideas never overwrites the Game.
25. As the Owner, I want Variations nested inside Variations, so that I can record deep analysis.
26. As the Owner, I want to promote a Variation to the main line, so that I can fix moves I recorded incorrectly.
27. As the Owner, I want to delete a Variation, or delete everything from a move onwards, so that I can clean up analysis.
28. As the Owner, I want to add a text comment to any move, so that I can write down my thoughts.
29. As the Owner, I want to add symbols such as `!`, `?`, `!?`, `±` and `=` to moves, so that I can mark moves the standard way.
30. As the Owner, I want the notation panel to show the main line, Variations, comments and symbols in a readable layout, so that an annotated Game reads like a book.
31. As the Owner, I want to step through a Game with arrow keys and by clicking moves in the notation, so that replaying is fast.
32. As the Owner, I want to flip the board, and have it show from my side by default, so that I see Games the way I played them.

### PGN import (phase 1)

33. As the Owner, I want to import a PGN file with many Games, so that my existing 100 Games get in at once.
34. As the Owner, I want to paste PGN text, so that I can import a single Game from anywhere.
35. As the Owner, I want imported Variations, comments and symbols kept, so that my existing annotations survive.
36. As the Owner, I want German piece letters (K D T L S) in imported moves converted to English, so that Games written in German notation import correctly.
37. As the Owner, I want Games where one side matches my name or an Alias assigned to me automatically, so that most imports need no attention.
38. As the Owner, I want Games where neither side matches me to go to a review list where I pick my side, so that nothing is assigned to me wrongly.
39. As the Owner, I want imported Games without a result to go to the review list, so that every Game in my Collection is complete.
40. As the Owner, I want Opponent names that closely match an existing Player (e.g. "Huber T." vs "Huber, Thomas") suggested as the same person, so that I can merge them with one click.
41. As the Owner, I want Players never merged without my confirmation, so that two different people with similar names stay separate.
42. As the Owner, I want imported Games that duplicate existing ones flagged, so that importing the same file twice doesn't double my Collection.
43. As the Owner, I want a missing or zero Elo in PGN imported as unrated, so that imports follow the same rule as manual entry.
44. As the Owner, I want a summary after import (imported, needing review, duplicates, failed with reason), so that I know exactly what happened.

### Game list and search (phase 1)

45. As the Owner, I want a list of all my Games with date, Opponent, my colour, Elos, result, Event and ECO, so that I can see my Collection at a glance.
46. As the Owner, I want to sort the list by any column, so that I can find Games quickly.
47. As the Owner, I want to filter by Opponent, Event, date range, result, my colour, ECO range, Tag and time control, so that I can narrow down to the Games I care about.
48. As the Owner, I want to combine filters, so that I can ask things like "my losses with Black in the club league in 2025".
49. As the Owner, I want to open a Game from the list with one click, so that browsing and replaying flow together.

### Interactive Analysis (phase 1)

50. As the Owner, I want to switch the Engine on while viewing any position, so that I see its evaluation and best lines.
51. As the Owner, I want up to 3 best lines shown with evaluation and depth, updating live, so that I see the alternatives as the Engine thinks.
52. As the Owner, I want an evaluation bar beside the board, so that I can read the position's balance at a glance.
53. As the Owner, I want the Engine to follow me as I step through moves, so that I don't have to restart it at every position.
54. As the Owner, I want to click an Engine line to play it on the board as a Variation, so that I can explore and keep its ideas.
55. As the Owner, I want the Engine to stop when I switch it off or leave the Game, so that my CPU isn't busy for nothing.

### Game Analysis (phase 1)

56. As the Owner, I want to start Game Analysis on a Game and keep using the app meanwhile, so that analysis doesn't block me.
57. As the Owner, I want to see progress while it runs, so that I know how long it will take.
58. As the Owner, I want to queue several Games for Game Analysis, so that I can analyse a whole tournament overnight.
59. As the Owner, I want an evaluation graph over the whole Game, so that I can see where it turned.
60. As the Owner, I want each move classified as inaccuracy, mistake or blunder based on how much winning chance it lost, so that a wild move in an already-won position isn't called a blunder.
61. As the Owner, I want the Engine's best move shown next to each classified move, so that I know what I should have played.
62. As the Owner, I want an Accuracy percentage for both sides, so that I can compare my play across Games.
63. As the Owner, I want the Engine Assessment kept separate from my Annotations, so that my own commentary stays mine.
64. As the Owner, I want to copy chosen parts of the Engine Assessment into the Game as Annotations and Variations, so that I can keep the insights I agree with.
65. As the Owner, I want the Engine Assessment saved, so that reopening a Game never needs re-analysis.
66. As the Owner, I want to re-run Game Analysis with different settings, replacing the old Engine Assessment, so that I can get a deeper verdict when I need one.
67. As the Owner, I want to see which Games have been analysed in the game list, so that I know what's left to review.

### Backups (phase 1)

68. As the Owner, I want the database copied to the backup folder automatically once a day, so that a disk failure or a bug can't wipe out my Games.
69. As the Owner, I want the 10 most recent backups kept, so that backups never fill my disk.
70. As the Owner, I want to export the whole Collection to PGN, so that my Games are never locked into this app.

### Opening Tree (phase 2)

71. As the Owner, I want the Opening Tree to show, for the current position, every move played in my Games with the number of Games and my score, so that I see how my lines have gone.
72. As the Owner, I want to click a move in the Opening Tree to play it, so that I can walk through my openings.
73. As the Owner, I want to see which Games reached the current position, so that I can open them.
74. As the Owner, I want a Masters source in the Opening Tree with move statistics from Master Games, so that I can compare my choices with what strong players do.
75. As the Owner, I want the top Master Games for the current position listed and viewable, so that I can learn from model games.
76. As the Owner, I want a clear message when the Masters source is unavailable (offline), so that I know why it's empty.

### Position Search (phase 2)

77. As the Owner, I want to set up or reach a position and find every Game where it occurred, so that I can find Games across different move orders.
78. As the Owner, I want Position Search to consider only the moves actually played, not Variations, so that results reflect real Games.
79. As the Owner, I want to open a search result at the matching position, so that I land right where it matters.

### Tablebase (phase 2)

80. As the Owner, I want endgame positions with 7 or fewer pieces to show the exact Tablebase result and best moves, so that I know the truth of an endgame I played.
81. As the Owner, I want Tablebase lookups to fail quietly when offline, so that nothing breaks without internet.

### Dossiers and statistics (phase 3)

82. As the Owner, I want a Dossier for each Opponent with my head-to-head score, so that I know how I do against them.
83. As the Owner, I want to see an Opponent's rating history from the Elo recorded in my Games, so that I know how they've developed.
84. As the Owner, I want to see which openings an Opponent played against me, so that I can prepare for our next Game.
85. As the Owner, I want to list all my Games against a Player, so that I can review them before a rematch.
86. As the Owner, I want to record a Player's FIDE ID and club, so that I can tell people apart and look them up.
87. As the Owner, I want to merge two Players later, so that I can fix duplicates I missed during import.
88. As the Owner, I want my own Dossier with my score by colour and by opening, so that I know my strong and weak openings.
89. As the Owner, I want my rating history charted from my Games, so that I see my progress.
90. As the Owner, I want my average opponent rating, with unrated Opponents left out, so that my statistics aren't distorted.
91. As the Owner, I want to see my Accuracy over time across analysed Games, so that I can tell whether I'm improving.

### Repertoire and training (phase 4)

92. As the Owner, I want to build a Repertoire for White and one for Black as move trees, so that I write down what I intend to play.
93. As the Owner, I want to add lines to my Repertoire from a Game or from the Opening Tree, so that building it doesn't mean retyping moves.
94. As the Owner, I want each Game to show its Deviation (where I or my Opponent left my Repertoire), so that I see where my preparation ended.
95. As the Owner, I want a list of Games where I deviated from my own Repertoire, so that I can find the lines I keep forgetting.
96. As the Owner, I want Repertoire Training to quiz me on my lines with spaced repetition, so that I remember them at the board.
97. As the Owner, I want lines I get wrong to come back sooner, so that practice targets my weak spots.
98. As the Owner, I want Mistake Training to show me positions from my Games where I made a mistake or blunder and ask for the better move, so that I learn from my own errors.
99. As the Owner, I want Mistake Training positions to use spaced repetition, so that solved positions fade out and missed ones return.
100. As the Owner, I want to open the source Game from a training position, so that I can see the context.

### Output (phase 5)

101. As the Owner, I want to export an annotated Game to PDF with board diagrams at moves I choose, so that I can print it or share it with a coach.
102. As the Owner, I want to copy the current position as an image to the clipboard, so that I can paste diagrams anywhere.
103. As the Owner, I want to export a single Game or the filtered list to PGN, so that I can share specific Games.
104. As the Owner, I want to add Leela Chess Zero as a second Engine running on my GPU, so that I get a different perspective from Stockfish.
105. As the Owner, I want to choose which Engine to use for Interactive Analysis and Game Analysis, so that I can compare their verdicts.
106. As the Owner, I want to run two Engines side by side in Interactive Analysis, so that I can see where they disagree.

## Implementation Decisions

### Architecture

- Desktop shell: an ASP.NET Core (.NET 11) backend hosting an Angular UI inside a desktop window (Photino.NET or WebView2; pick whichever integrates more simply). During development, the app also runs as a plain localhost web app. See ADR 0001.
- The UI talks to the backend over a REST-style HTTP API. Live Engine output (Interactive Analysis lines, Game Analysis progress) streams over a SignalR hub.
- Personal use only: no authentication, no installer requirement, Windows only. Stockfish (GPLv3) runs as a separate process and is never linked in, so the GPL question stays moot even if this changes.

### Modules (backend)

- **Chess Rules**: board representation, legal move generation, SAN parsing and formatting, FEN, position hashing (Zobrist), detection of check, checkmate and stalemate. This module is the single source of truth (ADR 0002). It may be written in-house or come from a library, but it must pass standard perft suites. Its SAN parser accepts German piece letters (K D T L S → K Q R B N) and always outputs English letters.
- **PGN**: parse and write PGN, including the Seven Tag Roster, Elo tags, nested Variations, comments and NAG symbols. Parsing a multi-game file yields per-game results (success or failure with a reason), so one bad Game never aborts an import.
- **Collection** (persistence): SQLite through EF Core, with migrations. It owns Games, the move tree, Players, Events, Tags, Settings, Engine Assessments and the position index, and in later phases Repertoires and training records.
- **Import**: Owner recognition via name and Aliases, the review list, Player match suggestions (normalised name similarity, e.g. surname plus initial), and duplicate detection (same Players, date and move sequence).
- **Opening Classification**: maps a move sequence to an ECO code and opening name using the Lichess chess-openings dataset (CC0), bundled with the app. It picks the deepest matching position.
- **Engine Host**: launches and manages UCI engine processes, applies the configured options (Threads, Hash, MultiPV), and exposes two use cases: an Interactive Analysis session (set a position, stream info lines, stop) and Game Analysis jobs (a background queue that processes one Game at a time and reports progress). It supports multiple configured engines from the start, though only Stockfish ships in phase 1.
- **Assessment**: turns raw Engine evaluations into an Engine Assessment. It converts centipawns and mate scores to winning chance using the Lichess formula, classifies each move by the winning chance it loses (inaccuracy ≥ 10, mistake ≥ 20, blunder ≥ 30 points on Lichess's −100…+100 winning-chance scale, as Lichess classifies; that is 5, 10 and 15 percentage points of winning chance), and computes Accuracy per side using the Lichess accuracy method. Pure functions, no I/O.
- **Research** (phase 2): an Opening Tree query over the position index, Position Search over the position index, a Masters client (Lichess masters opening explorer) and a Tablebase client (Lichess tablebase API). Both external clients sit behind interfaces so they can be faked, time out quickly, and return "unavailable" rather than throwing.
- **Statistics** (phase 3): Dossier and Owner statistics queries.
- **Training** (phase 4): Repertoire trees, Deviation detection, and spaced-repetition scheduling (an SM-2-style algorithm) for Repertoire Training and Mistake Training items.
- **Export** (phase 5): PDF rendering of an annotated Game with diagrams, PNG diagram rendering, and PGN export of a selection.
- **Backup**: a daily copy of the SQLite file (using SQLite's online backup, so it is consistent while the app runs) to the configured folder, keeping the 10 most recent copies.

### Domain and data model

- **Game**: Owner's colour, Opponent (a reference to a Player), White Elo and Black Elo (each a number or unrated; 0 or empty input means unrated), result (1-0, 0-1, ½-½; `*` is not allowed in the Collection), optional Event (reference), Site, Date (partial dates such as "2024" or "2024-03" allowed, as in PGN), Round, time control, private notes, Tags (many-to-many), the ECO code and opening name (derived), and the move tree. The Owner is not stored as a Player row. The Owner's side is implied by the Owner's colour.
- **Move tree**: stored as nodes. Each node has a parent, its order among siblings (the first child is the main continuation), the move, a comment before and after, and NAGs. Promoting, deleting and inserting Variations are tree operations validated by Chess Rules.
- **Position index**: one row per main-line position of each Game: position hash, Game, ply, and the move played from that position. Variations are excluded. This drives the Opening Tree (My games), Position Search and Deviation checks. It is rebuilt for a Game whenever its main line changes.
- **Engine Assessment**: belongs to one Game. It records the engine name and version and the settings used, plus per-ply records: evaluation before and after, best move and line, and Move Classification, together with Accuracy per side. Re-running replaces it. It never writes into the move tree. Copying into the Game is an explicit operation that creates Annotations and Variations (ADR 0003).
- **Player**: name, optional FIDE ID and club. Merging Players re-points their Games.
- **Settings**: Owner name, Aliases, engine definitions (path, options), Game Analysis time per move, backup folder.

### API shape (resource level)

- Games: list with filter and sort, get, create, update metadata, delete; move tree operations (add move, which automatically creates a Variation when it differs from the existing continuation; promote; delete Variation; delete from here; set comment; set NAGs).
- Players: list with search for autocomplete, create, update, merge. Events and Tags: list for autocomplete.
- Import: submit a PGN file or text; get the import result; resolve review-list items (choose the Owner's side, set the result, confirm or reject Player matches).
- Analysis: queue Game Analysis for one or many Games; get the Engine Assessment; copy parts of an assessment into the Game. Hub: start, update and stop an Interactive Analysis session; receive info lines and Game Analysis progress.
- Research, Statistics, Training and Export endpoints get added in their phases, following the same conventions.
- Every mutating call persists immediately (autosave). The UI debounces free-text edits (comments, notes).

### UI (Angular)

- Board: chessground. Instant legality feedback: chess.js. The backend's answer is authoritative.
- Main screens: the game list (filters and sort), the Game view (board, notation, metadata panel, Engine panel, evaluation graph), Settings, and the import review list. Later screens: Opening Tree panel, Position Search, Dossier, Repertoire editor, training sessions.
- The board shows from the Owner's colour by default.

### Engine defaults

- Interactive Analysis: Stockfish, 16 threads, 8 GB hash, MultiPV 3, no time limit.
- Game Analysis: Stockfish, 16 threads, 8 GB hash, MultiPV 1, 2 seconds per position.

## Testing Decisions

- **Good tests check behaviour through a public interface**, the way a real caller would, and assert on observable results: API responses, persisted state read back through the API, and streamed messages. They don't reach into internal classes, private methods or table layouts, so the internals can be refactored freely.
- **Primary seam: the backend HTTP and SignalR API.** Tests run the real backend in-process (ASP.NET Core test host) against a fresh temporary SQLite file per test. This covers Chess Rules, PGN, Import, Opening Classification, Collection, Assessment, Research and Statistics through the calls the UI makes. Example scenarios: importing a PGN file with German piece letters, an unknown Owner side, a missing result, a near-duplicate Opponent and a duplicate Game, then checking the import summary and the review list; adding a move that differs from the main line and checking that a Variation appears in the tree; checking that Elo 0 reads back as unrated.
- **Second seam: the UCI engine process.** Tests point the Engine Host at a scripted fake UCI engine, a small console program that answers with canned `info` and `bestmove` output per position. Engine Assessments, Move Classifications and Accuracy are then deterministic and can be asserted exactly. One optional, slow test category runs the real Stockfish at low depth as a smoke check.
- **External HTTP services** (Lichess Masters, Tablebase) are replaced by fake HTTP handlers, including timeout and offline cases.
- **The one exception to the API seam:** Chess Rules gets direct perft tests (standard positions and depths) as required by ADR 0002, plus SAN round-trip tests. A rules bug is catastrophic, and perft is the established way to catch it.
- **UI:** a small Playwright smoke suite against the real app with the fake engine. It covers entering a Game, importing PGN, stepping through moves and running Game Analysis. No unit tests for Angular components unless logic grows that warrants them.
- **Prior art:** none. The repository has no code yet. The first phase sets the conventions above.

## Out of Scope

- Importing online games from Lichess or Chess.com accounts.
- A local reference database (TWIC, Caissabase, Mega Database) and any Reference Game concept. Master Games come only from the online Masters source and are never stored.
- Games the Owner did not play (watched or master games) in the Collection.
- Incomplete Games: a result is mandatory. Games from custom starting positions, Chess960 or handicap games.
- Local Syzygy tablebase files. Tablebase lookups are online only.
- German UI and German piece letters in display or export. German letters are accepted on import only.
- Multiple databases or collections, multiple users, accounts, sync, cloud or mobile.
- Distribution, installers, auto-update, and GPL compliance work for redistribution.
- Engine-vs-engine matches, playing against the Engine, and online play.

## Further Notes

- **Phase order** is a delivery sequence, not a priority ranking between later phases. Each phase should end with the app usable for the stories delivered so far.
- **Lichess APIs:** check the current terms and authentication requirements of the masters opening explorer and tablebase endpoints at phase 2. If they require a personal API token, store it in Settings. If availability is unreliable, the Masters source degrades to "unavailable" without affecting anything else.
- **Lc0** needs a network weights file and a GPU backend (CUDA). Setting it up is a phase 5 task. The Engine Host's multi-engine support from phase 1 should mean no redesign is needed.
- **ADR 0003** (Engine Assessment kept separate) is up for review by the Owner once phase 1 is in use.
- **Scale assumption:** fewer than 10,000 Games. Simple indexed SQLite queries are sufficient, so there's no need for specialised position-search structures.
