# Chess Database

A personal, ChessBase-like desktop app for one over-the-board player's own Games. Games are entered on a board or imported from PGN, replayed with Variations and Annotations, and analysed with Stockfish. Everything lives in one local SQLite file.

It is an ASP.NET Core backend that hosts an Angular UI in a desktop window (Photino, which uses WebView2 on Windows). During development the same UI also runs in a normal browser.

## Status

Built ticket by ticket from [docs/tickets/tickets.md](docs/tickets/tickets.md). What works so far:

- The desktop window opens on the game list. The Collection is created and migrated automatically on first start.
- Chess Rules (`src/ChessDatabase.Rules`): legal move generation (passes the standard perft suite), check, checkmate and stalemate, FEN, SAN (German piece letters K D T L S are accepted, English letters are always written) and Zobrist position hashes.
- Entering a Game: set your name under Settings, then New Game: your colour, the Opponent (autocompleted, or created on the spot), both Elos (empty or 0 = unrated) and the result. Play the moves on the board by drag or click. Everything saves as you go. Games show up in the game list and can be deleted from the Game view.
- Replaying and Variations: step through a Game with the arrow keys (← back, → forward, Home/↑ start, End/↓ end of the line) or by clicking moves. F flips the board. Playing a different move from the recorded one starts a Variation, and Variations can nest. Right-click a move to promote it to the main line, delete its Variation, or delete from there on.
- Annotations: select any move (main line or Variation). In the Annotation card, set a move symbol (! ? !! ?? !? ?!) and a position symbol (= ∞ ⩲ ⩱ ± ∓ +- -+), and write a comment. Comments save shortly after you stop typing. They may span several lines, but can't contain "}", which PGN has no way to store. Symbols and comments show inline in the notation.
- Game details: Event and Site (both autocompleted from earlier Games), Date (2024, 2024-03 or 2024-03-15), Round, time control, private notes and free-form Tags. The ECO code and opening name come automatically from the main line, using the bundled Lichess opening dataset (CC0, `src/ChessDatabase.Api/Openings/Data`), and follow it when it changes. The game list shows Event and ECO.
- PGN import (Import tab): choose a PGN file or paste PGN with any number of Games. Variations, comments and symbols are kept, and German piece letters become English. A Game is yours when one side matches your name in any usual form ("Mustermann, Max", "Mustermann M.", "Max Mustermann") or one of the Aliases listed in Settings. The other side becomes the Opponent: an existing Player with exactly that name, otherwise a new Player. Missing or 0 Elos import as unrated. Games with no matching side or no result are held back as "needs review". Malformed Games fail with a reason and never stop the rest. Files may be UTF-8 or Latin-1.
- Import review list (Import → Review list): held-back Games wait here until you choose your side and/or the result. An Opponent whose name only resembles an existing Player ("Huber T." / "Huber, Thomas") is offered as a suggestion: confirm to link that Player, or say no to create a new one. Nothing is linked without your confirmation. Games already in the Collection (same Players, date and moves) are reported as duplicates and skipped. Each one can still be imported anyway, and resolving a review item that is already in the Collection asks before adding it again. An entry without a name for White or Black fails with a reason.
- PGN export: "Export PGN" under the board saves one Game, and "Export to PGN" on the game list saves the whole Collection. The output is standard PGN with English piece letters: the Seven Tag Roster, Elo tags for rated sides only, the other details, Variations, comments and symbols ($n). Your Tags and notes travel in extra `Tags` and `Notes` tags, so exporting and re-importing loses nothing.
- Interactive Analysis: in a Game, switch the Engine on (the switch under the board, or E). It shows up to 3 best lines with evaluation and depth, updating live, and an evaluation bar beside the board. Scores are from White's side. It follows you as you step through the Game, and stops when you switch it off or leave the Game. Clicking a line adds it to the Game as a Variation at the board position. Settings → Engine holds the Stockfish program path (download it from stockfishchess.org; the path is checked when you save), threads, hash, the number of lines (1 to 3) and an optional time limit per position (defaults 16 threads, 8 GB hash, 3 lines, no time limit), plus the Game Analysis time per position.
- Game Analysis: in a Game, "Analyse Game" (in the Game Analysis card) has the Engine go through every position of the main line, 2 seconds each by default (MultiPV 1, with the Interactive Analysis threads and hash). It runs in the background with progress shown, so you can keep working. The result, the Engine Assessment, is saved and shown again when you reopen the Game: Accuracy for both sides, an evaluation graph (click it to jump to a position), and every inaccuracy, mistake and blunder marked under its move in the scoresheet, with the Engine's better move and line. Moves are classified by how much winning chance they give away (Lichess's winning-chance formula and thresholds: 10, 20 and 30 points on its −100 to +100 scale), so a wild move in an already won position isn't called a blunder. Accuracy uses Lichess's method. The Engine Assessment is kept apart from your own Annotations and never changes the moves, symbols or comments. If the moves change afterwards, it says so; analyse again to update it.
- Copying from the Engine Assessment: next to each classified move, "Copy" writes it into the Game as your own Annotation: the symbol (?! for an inaccuracy, ? for a mistake, ?? for a blunder) and the Engine's line as a Variation. "Copy all mistakes and blunders" does the same for every mistake and blunder at once. A symbol you already gave the move and your comments are never replaced, and a line that is already in the Game isn't added twice. Copying is the only way anything from the Engine Assessment gets into the Game.
- Analysing many Games: tick Games in the game list (or all shown, e.g. a filtered tournament) and press "Analyse n Games". They run one after another; a strip under the top bar shows which Game of how many and its progress on every screen, and Cancel stops the queue (finished Engine Assessments are kept). The game list marks analysed Games and can filter by them. "Analyse again" in a Game, with a different time per position or after changing the Engine settings, replaces the old Engine Assessment; the card shows the Engine (name and version) and the settings used.
- Opening Tree: in a Game, the Opening Tree card shows every move played from the board position in your Games, with how many Games and your score (won/drawn/lost from your side). Positions reached by different move orders count together, and only main lines count, never Variations. Clicking a move steps into it if the Game has it; otherwise the board shows it without saving anything (exploring: carry on in the tree or on the board, ← steps back, "Add to Game" keeps the moves as a Variation, "Back to the Game" drops them). The newest Games that reached the position are listed below (up to 50), and opening one shows it at that position. The tree follows every change to a main line at once.
- Masters source: the Opening Tree's "Masters" tab shows, for the board position, what masters played there (from the Lichess masters explorer, explorer.lichess.org): each move with its number of games and White/draw/Black percentages (the 12 most played, plus how many games went another way), and the top Master Games. A Master Game opens read-only on its own page and is never added to your Collection. Clicking a Masters move shows it on the board like an Opening Tree move. It needs the internet: when Lichess can't be reached, is slow (5 s) or refuses, the tab says "Masters unavailable" with the reason and nothing else is affected. Answers are kept while the app runs, and only one request goes to Lichess at a time. Lichess currently answers without logging in; if it starts asking for a token, create a personal one at lichess.org/account/oauth/token (no permissions needed) and add it under Settings → Lichess. The token is stored encrypted for your Windows account in `lichess-token.bin` beside the Collection, never in the Collection itself, so backups don't carry it (on another PC, enter it again).
- Tablebase: when the board shows 7 pieces or fewer (kings included), a Tablebase card gives the exact verdict from the Lichess tablebase (tablebase.lichess.org, no token needed): who wins or whether it's a draw, with the distance to zeroing (DTZ) and to mate (DTM) in half-moves, and every move ranked best first with its own verdict. Clicking a move shows it on the board. Positions with more pieces show nothing and send nothing. Offline or when Lichess fails, the card just says "Tablebase unavailable". The card also appears on the Position Search page for a set-up endgame.
- Position Search (Position Search tab): find every Game whose main line reached a position, by any move order. "Search position" under the board in a Game searches the position on the board; the editor sets up any position (drag pieces, drag them off to remove, place new ones from the palette, side to move, castling). Positions that can't occur, such as a pawn on the first rank or the side not to move in check, are refused with the reason. Each result shows the move number, and opening it shows the Game at that position. Positions that only occur in Variations are not found.
- Players and Dossiers (Players tab, or click an Opponent's name in the game list or a Game): every Player with your score against them, searchable. A Player's Dossier shows your head-to-head score (won/drawn/lost from your side), their rating history charted from the Elo recorded in your Games (unrated Games and Games without a date are left out; hover for values, click a point to open that Game, or show it as a table), the openings of your Games against them by ECO with your score, and all those Games. You can record their FIDE ID and club. Two Player records that are really one person can be merged ("Merge…" in the Dossier or the Players list): pick the other Player, choose which name, FIDE ID and club to keep, and confirm; all Games move to the kept Player and the other record is removed. Nothing is merged without that confirmation.
- Daily backups: on the first start of each day, and every 24 hours while the app runs, the Collection is copied (with SQLite's online backup, so the copy is consistent) into the backup folder. Only the 10 newest copies are kept. Settings → Backups shows the folder and the last backup, lets you choose another folder (another drive is safer), and has "Back up now".
- Game list: click a column header to sort (click again to reverse; empty values always go last). Filter by Opponent, Event, date range (partial dates count when they overlap), result from your side (won/drawn/lost), your colour, ECO range, Tag, time control and whether a Game is analysed. Filters combine and stay in the URL. "Export shown to PGN" exports exactly the filtered list.

## Prerequisites

- Windows 10/11 with the WebView2 runtime (part of current Windows and Edge)
- [.NET 11 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 24 or later (npm 10)
- Microsoft Edge, which the Playwright smoke tests drive (no separate browser download needed)
- PowerShell 7 (`pwsh`) for `build.ps1`

## Running

| What | Command |
|---|---|
| Desktop app | `./build.ps1 run` |
| Browser (development) | `./build.ps1 dev`, then open <http://localhost:4200> |

`./build.ps1 run` builds the Angular UI, then starts `src/ChessDatabase.Desktop`. That project serves the UI and the API on a private loopback port and opens a window on it.

`./build.ps1 dev` starts the backend on <http://localhost:5080> and `ng serve` on <http://localhost:4200>. The dev server proxies `/api` to the backend and reloads the UI as you edit it. You can also run the two halves yourself: `dotnet run --project src/ChessDatabase.Api` and `npm start` in `ui/`.

### Where the data lives

The Collection is `%LOCALAPPDATA%\ChessDatabase\collection.db`. It is created and migrated automatically on first start. To use a different file, set `Collection:DatabasePath` in `appsettings.json`, or pass `--Collection:DatabasePath=<path>` on the command line.

Backups go to `Backups` beside the Collection (by default `%LOCALAPPDATA%\ChessDatabase\Backups`) unless Settings name another folder. Each backup is a complete `collection-<date>-<time>.db`; to restore one, close the app and copy it over `collection.db`. Setting `Backup:Daily` to `false` turns the automatic backups off.

## Testing

```powershell
./build.ps1 test
```

This one command builds the UI and the backend and runs every test project. Once the UI is built, `dotnet test ChessDatabase.slnx` runs the same tests.

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
build.ps1                       build / test / run entry point
src/
  ChessDatabase.Rules/          Chess Rules: board, legal moves, SAN, FEN, Zobrist hashing
  ChessDatabase.Api/            ASP.NET Core backend: HTTP API and the Collection (EF Core + SQLite)
    ChessDatabaseApp.cs         composes the app; shared by browser mode, desktop shell and tests
    Collection/                 DbContext, entities, migrations
    Engines/                    Engine Host: UCI processes, Interactive Analysis hub (SignalR)
    Analysis/                   Game Analysis: background queue, analyser, Engine Assessment API
    Assessment/                 winning chance, Move Classification and Accuracy (pure functions)
    Research/                   position index, Opening Tree and Position Search
    Backup/                     daily backups
  ChessDatabase.Desktop/        Photino desktop shell (builds ChessDatabase.exe)
ui/                             Angular UI (chessground board, chess.js for instant feedback)
tests/                          see Testing
docs/                           spec, ADRs and tickets
```

Database migrations are created with the local EF tool, for example `dotnet ef migrations add <Name> -p src/ChessDatabase.Api -o Collection/Migrations`. Run `dotnet tool restore` once first.

## Documents

- [Glossary](GLOSSARY.md): the domain terms (Game, Owner, Variation, Annotation, …) used everywhere, code included
- [Spec](docs/specs/0001-chess-database.md)
- ADRs: [0001 desktop shell](docs/adr/0001-aspnet-core-angular-desktop-shell.md), [0002 Chess Rules in C#](docs/adr/0002-csharp-is-source-of-truth-for-chess-rules.md), [0003 Engine Assessment kept separate](docs/adr/0003-engine-assessment-separate-from-annotations.md)
- [Tickets](docs/tickets/tickets.md)
- [Product](PRODUCT.md) and the [design direction for the review screen](.impeccable/surfaces/review.md)
