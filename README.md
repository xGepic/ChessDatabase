# Chess Database

A personal, ChessBase-like web app for one over-the-board player's own Games. Games are entered on a board or imported from PGN, replayed with Variations and Annotations, and analysed with Stockfish. Everything lives in one local SQLite file.

It is an ASP.NET Core backend and an Angular UI that run on your own PC and are used in a normal browser at <http://localhost:4200>. Nothing is reachable from other machines.

## Status

Built ticket by ticket from [docs/tickets/tickets.md](docs/tickets/tickets.md). What works so far:

- The app opens on the game list. The Collection is created and migrated automatically on first start.
- Chess Rules (`src/ChessDatabase.Rules`): legal move generation (passes the standard perft suite), check, checkmate and stalemate, FEN, SAN (German piece letters K D T L S are accepted, English letters are always written) and Zobrist position hashes.
- Entering a Game: set your name under Settings, then New Game: your colour, the Opponent (autocompleted, or created on the spot), both Elos (empty or 0 = unrated) and the result. Play the moves on the board by drag or click. Everything saves as you go. Games show up in the game list and can be deleted from the Game view.
- Replaying and Variations: step through a Game with the arrow keys (← back, → forward, Home/↑ start, End/↓ end of the line) or by clicking moves. F flips the board. Playing a different move from the recorded one starts a Variation, and Variations can nest. Right-click a move to promote it to the main line, delete its Variation, or delete from there on.
- Annotations: select any move (main line or Variation). In the Annotation card, set a move symbol (! ? !! ?? !? ?!) and a position symbol (= ∞ ⩲ ⩱ ± ∓ +- -+), and write a comment. Comments save shortly after you stop typing. They may span several lines, but can't contain "}", which PGN has no way to store. Symbols and comments show inline in the notation.
- Game details: Event and Site (both autocompleted from earlier Games), Date (2024, 2024-03 or 2024-03-15), Round, time control, private notes and free-form Tags. The ECO code and opening name come automatically from the main line, using the bundled Lichess opening dataset (CC0, `src/ChessDatabase.Api/Openings/Data`), and follow it when it changes. The game list shows Event and ECO.
- PGN import (Import tab): choose a PGN file or paste PGN with any number of Games. Variations, comments and symbols are kept, and German piece letters become English. A Game is yours when one side matches your name in any usual form ("Mustermann, Max", "Mustermann M.", "Max Mustermann") or one of the Aliases listed in Settings. The other side becomes the Opponent: an existing Player with exactly that name, otherwise a new Player. Missing or 0 Elos import as unrated. Games with no matching side or no result are held back as "needs review". Malformed Games fail with a reason and never stop the rest. Files may be UTF-8 or Latin-1.
- Import review list (Import → Review list): held-back Games wait here until you choose your side and/or the result. An Opponent whose name only resembles an existing Player ("Huber T." / "Huber, Thomas") is offered as a suggestion: confirm to link that Player, or say no to create a new one. Nothing is linked without your confirmation. Games already in the Collection (same Players, date and moves) are reported as duplicates and skipped. Each one can still be imported anyway, and resolving a review item that is already in the Collection asks before adding it again. An entry without a name for White or Black fails with a reason.
- PGN export: "Export PGN" under the board saves one Game, and "Export to PGN" on the game list saves the whole Collection. The output is standard PGN with English piece letters: the Seven Tag Roster, Elo tags for rated sides only, the other details, Variations, comments and symbols ($n). Your Tags and notes travel in extra `Tags` and `Notes` tags, so exporting and re-importing loses nothing.
- Interactive Analysis: in a Game, switch the Engine on (the switch under the board, or E). It shows up to 3 best lines with evaluation and depth, updating live, and an evaluation bar beside the board. Scores are from White's side. It follows you as you step through the Game, and stops when you switch it off or leave the Game. Clicking a line adds it to the Game as a Variation at the board position. Settings → Engines holds the Stockfish program path (download it from stockfishchess.org; "Browse…" finds the .exe in the usual Windows "Open" dialog, which the backend opens on this PC and keeps in front, or paste the path; it is checked when you save, and Browse… saves at once), threads, hash, the number of lines (1 to 3) and an optional time limit per position (defaults 16 threads, 8 GB hash, 3 lines, no time limit), plus the Game Analysis time per position.
- More Engines, such as Leela Chess Zero: Settings → Engines → "Add Lc0" (or "Add other engine" for any UCI program). Each Engine has its own program, threads, hash, lines and time settings, plus further UCI options as name/value pairs. The Lc0 template starts with 2 threads and the options WeightsFile (the network file from lczero.org, e.g. `C:\Nets\BT4.pb.gz`) and Backend `cuda-auto` (the NVIDIA GPU; `cuda-fp16` also suits an RTX card). Only options the program announces are sent, so Lc0 never gets Stockfish's Hash. If an Engine quits while loading (a wrong weights file, say), the message says so. At the top of the card, choose which Engine Interactive Analysis uses and which one Game Analysis uses (Mistake Training judges your answers with the Engine that analysed the Game, as long as it is still configured, since Engines score on different scales). Engines can be removed; whatever used a removed one switches to the first remaining, and the last one stays. The Engine Assessment card names the Engine (and version) that produced it. Changing an Engine in Settings, or which one Interactive Analysis uses, takes effect on the next move in an open Game.
- Game Analysis: in a Game, "Analyse Game" (in the Game Analysis card) has the Game Analysis Engine (chosen in Settings → Engines) go through every position of the main line, 2 seconds each by default (MultiPV 1, with that Engine's threads and hash). It runs in the background with progress shown, so you can keep working. The result, the Engine Assessment, is saved and shown again when you reopen the Game: Accuracy for both sides, an evaluation graph (click it to jump to a position), and every inaccuracy, mistake and blunder marked under its move in the scoresheet, with the Engine's better move and line. Moves are classified by how much winning chance they give away (Lichess's winning-chance formula and thresholds: 10, 20 and 30 points on its −100 to +100 scale), so a wild move in an already won position isn't called a blunder. Accuracy uses Lichess's method. The Engine Assessment is kept apart from your own Annotations and never changes the moves, symbols or comments. If the moves change afterwards, it says so; analyse again to update it.
- Copying from the Engine Assessment: next to each classified move, "Copy" writes it into the Game as your own Annotation: the symbol (?! for an inaccuracy, ? for a mistake, ?? for a blunder) and the Engine's line as a Variation. "Copy all mistakes and blunders" does the same for every mistake and blunder at once. A symbol you already gave the move and your comments are never replaced, and a line that is already in the Game isn't added twice. Copying is the only way anything from the Engine Assessment gets into the Game.
- Analysing many Games: tick Games in the game list (or all shown, e.g. a filtered tournament) and press "Analyse n Games". They run one after another; a strip under the top bar shows which Game of how many and its progress on every screen, and Cancel stops the queue (finished Engine Assessments are kept). The game list marks analysed Games and can filter by them. "Analyse again" in a Game, with a different time per position or after changing the Engine settings, replaces the old Engine Assessment; the card shows the Engine (name and version) and the settings used.
- Opening Tree: in a Game, the Opening Tree card shows every move played from the board position in your Games, with how many Games and your score (won/drawn/lost from your side). Positions reached by different move orders count together, and only main lines count, never Variations. Clicking a move steps into it if the Game has it; otherwise the board shows it without saving anything (exploring: carry on in the tree or on the board, ← steps back, "Add to Game" keeps the moves as a Variation, "Back to the Game" drops them). The newest Games that reached the position are listed below (up to 50), and opening one shows it at that position. The tree follows every change to a main line at once.
- Masters source: the Opening Tree's "Masters" tab shows, for the board position, what masters played there (from the Lichess masters explorer, explorer.lichess.org): each move with its number of games and White/draw/Black percentages (the 12 most played, plus how many games went another way), and the top Master Games. A Master Game opens read-only on its own page and is never added to your Collection. Clicking a Masters move shows it on the board like an Opening Tree move. It needs the internet: when Lichess can't be reached, is slow (5 s) or refuses, the tab says "Masters unavailable" with the reason and nothing else is affected. Answers are kept while the app runs, and only one request goes to Lichess at a time. Lichess currently answers without logging in; if it starts asking for a token, create a personal one at lichess.org/account/oauth/token (no permissions needed) and add it under Settings → Lichess. The token is stored encrypted for your Windows account in `lichess-token.bin` beside the Collection, never in the Collection itself, so backups don't carry it (on another PC, enter it again).
- Tablebase: when the board shows 7 pieces or fewer (kings included), a Tablebase card gives the exact verdict from the Lichess tablebase (tablebase.lichess.org, no token needed): who wins or whether it's a draw, with the distance to zeroing (DTZ) and to mate (DTM) in half-moves, and every move ranked best first with its own verdict. Clicking a move shows it on the board. Positions with more pieces show nothing and send nothing. Offline or when Lichess fails, the card just says "Tablebase unavailable". The card also appears on the Position Search page for a set-up endgame.
- Position Search (Position Search tab): find every Game whose main line reached a position, by any move order. "Search position" under the board in a Game searches the position on the board; the editor sets up any position (drag pieces, drag them off to remove, place new ones from the palette, side to move, castling). Positions that can't occur, such as a pawn on the first rank or the side not to move in check, are refused with the reason. Each result shows the move number, and opening it shows the Game at that position. Positions that only occur in Variations are not found.
- Players and Dossiers (Players tab, or click an Opponent's name in the game list or a Game): every Player with your score against them, searchable. A Player's Dossier shows your head-to-head score (won/drawn/lost from your side), their rating history charted from the Elo recorded in your Games (unrated Games and Games without a date are left out; hover for values, click a point to open that Game (or focus the chart, step through the points with the arrow keys and press Enter), or show it as a table), the openings of your Games against them by ECO with your score, and all those Games. You can record their FIDE ID and club. Two Player records that are really one person can be merged ("Merge…" in the Dossier or the Players list): pick the other Player, choose which name, FIDE ID and club to keep, and confirm; all Games move to the kept Player and the other record is removed. Nothing is merged without that confirmation.
- My statistics (My statistics tab): your overall score, your score with White and with Black and by opening (ECO) and colour, your rating history from your own Elo in each Game, your average opponent rating (unrated Opponents left out), and your Accuracy over time across analysed Games. A date range (partial dates allowed) and a time control limit every figure, exactly as they do in the game list. Click a point in a chart to open its Game.
- Repertoire (Repertoire tab): one Repertoire for White and one for Black, each a move tree edited like a Game. Play moves on the board to add them (a different move where one is already there becomes a Variation), right-click a move to promote or delete it, and comment any move; everything saves at once. "Add to Repertoire" under the board in a Game, or "+ Repertoire" in the Opening Tree, adds the moves up to the board position (explored moves included) to the Repertoire for the colour you had in that Game, merging with the branches already there.
- Deviation check: every Game shows, under the players, where it left your Repertoire for the colour you had: who left it (you or your Opponent) with which move and what your Repertoire plays there, "End of your Repertoire" when the Repertoire had no move for the position, or "In your Repertoire to the end". The move is outlined in the notation; click the line to go to it. It works by position, so another move order that comes back into your Repertoire counts as in-book. "My Deviations" (on the Repertoire page) lists the Games where you left your own Repertoire, with the move you played and the one your Repertoire expected, filterable by colour and date; click a row to open the Game at that move.
- Repertoire Training (Training tab): drills your Repertoire with spaced repetition. Every Repertoire position where it is your move becomes a training item; new lines become new items automatically and removed ones go. The app shows the line and plays your Opponent's last move, then you play your Repertoire move on the board (any of your Repertoire's moves there counts). A wrong answer shows the right move and comes back a minute later, in the same session; a right one comes back later and later (SM-2 style: 1 day, 6 days, then the interval times the item's ease, which drops a little with every miss). The number of items due today is shown; Enter goes on to the next.
- Mistake Training (Training tab, Mistakes): every move of yours that Game Analysis classified as a mistake or blunder becomes a position to solve: the board shows the position before it, with what you played ("You played 2.Qh5??"), and you look for a better move. The Engine's best move is right straight away; any other move is judged by the Engine (as in Game Analysis, with the time per position that analysis had) and counts as right when it loses less winning chance than an inaccuracy would. Afterwards the Engine's best line is shown, and "Open source Game" opens the Game at that position. Only your own mistakes are used, never your Opponent's. Positions are scheduled with the same spaced repetition as Repertoire Training. Re-running Game Analysis keeps the progress of positions that are still mistakes; positions that no longer are drop out. If you change a Game's moves before a mistake, that position leaves training until you run Game Analysis on the Game again (so "Open source Game" always shows the same position); changes after it don't matter. Without a working Engine, only the best move or the Game's own move can be judged.
- Daily backups: on the first start of each day, and every 24 hours while the app runs, the Collection is copied (with SQLite's online backup, so the copy is consistent) into the backup folder. Only the 10 newest copies are kept. Settings → Backups shows the folder and the last backup, lets you choose another folder (another drive is safer), and has "Back up now".
- Copy diagram: under the board in a Game, "Copy diagram" puts the board position on the clipboard as a PNG (480 × 480 pixels, 504 with coordinates), ready to paste into Word, an email or a chat. It shows the board the way it is turned (F flips it). The "coordinates" and "last move" boxes beside it add files and ranks and highlight the last move; both are remembered.
- PDF export: "Export PDF" under the board saves the Game as an A4 PDF to print or send to a coach: the Players with their Elos ("unrated" when there is none), Event, Site, Round, date, result and opening, then the notation (main line in bold, each Variation of it indented below, deeper Variations in parentheses, comments in italics, symbols with their moves; common signs from imported PGN such as ⨀ zugzwang, → attack and N novelty print too, and show in the app's notation). To get a board diagram after a move, select it and press "▦ Diagram here" in the Annotation card (the notation shows ▦; the mark is saved as NAG $220, so it survives PGN export and import). "final position" adds a diagram of the final position at the end. Diagrams are drawn from your side, with coordinates and the last move highlighted. Engine Assessments are never printed.
- Game list: click a column header to sort (click again to reverse; empty values always go last). Filter by Opponent, Event, date range (partial dates count when they overlap), result from your side (won/drawn/lost), your colour, ECO range, Tag, time control and whether a Game is analysed. Filters combine and stay in the URL. "Export shown to PGN" exports exactly the filtered list.

## Prerequisites

- Windows 10/11 (the Lichess token is encrypted for your Windows account, and PDF export uses the Windows fonts)
- [.NET 11 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 24 or later (npm 10)
- Microsoft Edge, which the Playwright smoke tests drive (no separate browser download needed)

## Running

Start the two halves in two terminals:

| What | Command | Address |
|---|---|---|
| Backend | `dotnet run --project src/ChessDatabase.Api` | <http://localhost:5080> |
| UI | `ng serve` (or `npm start`) in `ui/` | <http://localhost:4200> |

Then open <http://localhost:4200>. The UI dev server proxies `/api` and `/hubs` (SignalR) to the backend (`ui/proxy.conf.json`) and reloads as you edit. Run `npm ci` in `ui/` once first.

The backend only answers to `localhost` and `127.0.0.1`, and refuses changes sent from pages of other sites, so websites open in the same browser can't touch the Collection. There are no logins: don't expose port 5080 to the network.

### Where the data lives

The Collection is `%LOCALAPPDATA%\ChessDatabase\collection.db`. It is created and migrated automatically on first start. To use a different file, set `Collection:DatabasePath` in `appsettings.json`, or pass `--Collection:DatabasePath=<path>` on the command line.

Backups go to `Backups` beside the Collection (by default `%LOCALAPPDATA%\ChessDatabase\Backups`) unless Settings name another folder. Each backup is a complete `collection-<date>-<time>.db`; to restore one, stop the backend and copy it over `collection.db`. Setting `Backup:Daily` to `false` turns the automatic backups off.

## Testing

```powershell
dotnet test ChessDatabase.slnx
```

This runs every test project. Building the smoke test project builds the UI first (`npm run build` in `ui/`, after `npm ci` when `node_modules` is missing), because the smoke tests drive the built UI. The UI build is skipped when nothing under `ui/` changed since the last one; `-p:SkipUiBuild=true` skips it anyway.

| Project | What it tests |
|---|---|
| `tests/ChessDatabase.Rules.Tests` | Chess Rules directly: perft, SAN and FEN round trips, hashing (ADR 0002) |
| `tests/ChessDatabase.Api.Tests` | The HTTP API, run in-process against a fresh temporary SQLite file per test |
| `tests/ChessDatabase.Smoke.Tests` | Playwright drives the real app (built UI and backend) in headless Edge |
| `tests/ChessDatabase.FakeEngine` | Not a test project: a scripted UCI engine that answers with canned output per position, so Engine tests are exact |

The tests never need Stockfish. One optional check runs the real one: set `STOCKFISH_PATH` to the executable and run `dotnet test --filter Category=Stockfish`. Without the variable it is skipped.

## Project layout

```
ChessDatabase.slnx
src/
  ChessDatabase.Rules/          Chess Rules: board, legal moves, SAN, FEN, Zobrist hashing
  ChessDatabase.Api/            ASP.NET Core backend: HTTP API and the Collection (EF Core + SQLite)
    ChessDatabaseApp.cs         composes the app; shared by Program and the tests
    LocalOnly.cs                answers only to localhost and refuses cross-site changes
    Collection/                 DbContext, entities, migrations
    Engines/                    Engine Host: UCI processes, Interactive Analysis hub (SignalR)
    Analysis/                   Game Analysis: background queue, analyser, Engine Assessment API
    Assessment/                 winning chance, Move Classification and Accuracy (pure functions)
    Research/                   Opening Tree, Position Search, Masters and Tablebase (Lichess)
    Statistics/                 Dossiers and the Owner's statistics
    Training/                   Repertoires, the Deviation check, Repertoire and Mistake Training, the spaced-repetition scheduler
    Export/                     board diagrams (PNG, SkiaSharp) and PDF export (QuestPDF)
    Backup/                     daily backups
ui/                             Angular UI (chessground board, chess.js for instant feedback)
tests/                          see Testing
docs/                           spec, ADRs and tickets
```

Database migrations are created with the local EF tool, for example `dotnet ef migrations add <Name> -p src/ChessDatabase.Api -o Collection/Migrations`. Run `dotnet tool restore` once first.

## Documents

- [Glossary](GLOSSARY.md): the domain terms (Game, Owner, Variation, Annotation, …) used everywhere, code included
- [Spec](docs/specs/0001-chess-database.md)
- ADRs: [0001 desktop shell (superseded)](docs/adr/0001-aspnet-core-angular-desktop-shell.md), [0004 web app on localhost](docs/adr/0004-web-app-on-localhost.md), [0002 Chess Rules in C#](docs/adr/0002-csharp-is-source-of-truth-for-chess-rules.md), [0003 Engine Assessment kept separate](docs/adr/0003-engine-assessment-separate-from-annotations.md)
- [Tickets](docs/tickets/tickets.md)
- [Product](PRODUCT.md) and the [design direction for the review screen](.impeccable/surfaces/review.md)
