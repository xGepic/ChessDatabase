# Chess Database

A personal, ChessBase-like desktop app for one over-the-board player's own Games. Games are entered on a board or imported from PGN, replayed with Variations and Annotations, and analysed with Stockfish. Everything lives in one local SQLite file.

It is an ASP.NET Core backend that hosts an Angular UI in a desktop window (Photino, which uses WebView2 on Windows). During development the same UI also runs in a normal browser.

## Status

Built ticket by ticket from [docs/tickets/tickets.md](docs/tickets/tickets.md). What works so far:

- The desktop window opens on the game list. The Collection is created and migrated automatically on first start.
- Chess Rules (`src/ChessDatabase.Rules`): legal move generation (passes the standard perft suite), check, checkmate and stalemate, FEN, SAN (German piece letters K D T L S are accepted, English letters are always written) and Zobrist position hashes.
- Entering a Game: set your name under Settings, then New Game: your colour, the Opponent (autocompleted, or created on the spot), both Elos (empty or 0 = unrated) and the result. Play the moves on the board by drag or click. Everything saves as you go. Games show up in the game list and can be deleted from the Game view.
- Replaying and Variations: step through a Game with the arrow keys (← back, → forward, Home/↑ start, End/↓ end of the line) or by clicking moves. F flips the board. Playing a different move from the recorded one starts a Variation, and Variations can nest. Right-click a move to promote it to the main line, delete its Variation, or delete from there on.
- Annotations: select any move (main line or Variation). In the Annotation card, set a move symbol (! ? !! ?? !? ?!) and a position symbol (= ∞ ⩲ ⩱ ± ∓ +- -+), and write a comment. Comments save shortly after you stop typing. Symbols and comments show inline in the notation.
- Game details: Event and Site (both autocompleted from earlier Games), Date (2024, 2024-03 or 2024-03-15), Round, time control, private notes and free-form Tags. The ECO code and opening name come automatically from the main line, using the bundled Lichess opening dataset (CC0, `src/ChessDatabase.Api/Openings/Data`), and follow it when it changes. The game list shows Event and ECO.
- PGN import (Import tab): choose a PGN file or paste PGN with any number of Games. Variations, comments and symbols are kept, and German piece letters become English. A Game is yours when one side matches your name in any usual form ("Mustermann, Max", "Mustermann M.", "Max Mustermann") or one of the Aliases listed in Settings. The other side becomes the Opponent: an existing Player with exactly that name, otherwise a new Player. Missing or 0 Elos import as unrated. Games with no matching side or no result are held back as "needs review". Malformed Games fail with a reason and never stop the rest. Files may be UTF-8 or Latin-1.

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

## Project layout

```
ChessDatabase.slnx
build.ps1                       build / test / run entry point
src/
  ChessDatabase.Rules/          Chess Rules: board, legal moves, SAN, FEN, Zobrist hashing
  ChessDatabase.Api/            ASP.NET Core backend: HTTP API and the Collection (EF Core + SQLite)
    ChessDatabaseApp.cs         composes the app; shared by browser mode, desktop shell and tests
    Collection/                 DbContext, entities, migrations
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
