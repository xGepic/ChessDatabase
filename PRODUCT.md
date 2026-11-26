# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

The UI is an Angular app shown in a desktop window (Photino.NET or WebView2) on Windows. During development it also runs as a localhost web app. See docs/adr/0001-aspnet-core-angular-desktop-shell.md.

## Users

One person, the Owner: a hobby over-the-board chess player who builds and uses the app for themselves. They sit at their home PC with a large, high-resolution monitor, often in the evening after a tournament or league game. The job is to enter or import the games they just played, go through them with an engine, and over time learn from their own mistakes and openings.

## Product Purpose

A personal chess database in the spirit of ChessBase, cut down to what one club player needs. It holds only the Owner's own games (about 100 now, never more than 10,000) and analyses them with Stockfish, and later Lc0. Success means reviewing a fresh game takes minutes: type in or import the game, see where it went wrong, and carry the lesson into Repertoire and Mistake Training.

## Positioning

Every game in the app is one the Owner played, so every view (Opening Tree, Dossiers, Deviation check, Mistake Training) is about the Owner's own chess, not a reference database. Engine output is kept as a separate Engine Assessment and never mixed into the Owner's own annotations unless they copy it in (docs/adr/0003-engine-assessment-separate-from-annotations.md).

## Operating Context

- Workflows, in order of frequency: enter or import a game after playing it; replay it with Interactive Analysis; run Game Analysis and review its classifications and Accuracy; look up opponents before a game (Dossier); drill the Repertoire and own mistakes.
- Uses a powerful desktop machine (64 GB RAM, RTX 4090). Engine defaults are deliberately moderate: 16 threads, 8 GB hash, MultiPV 3.
- Online lookups: Lichess masters opening explorer and tablebase.
- PGN is how games come in and go out.

## Capabilities and Constraints

- Full scope, phases and the 30 tickets are in docs/specs/0001-chess-database.md and docs/tickets/tickets.md. Terms are in GLOSSARY.md and must be used as written there (Game, Variation, Annotation, Engine Assessment, Opening Tree, Dossier, and so on).
- The interface language is English. Moves are always shown with English piece letters (K Q R B N); German letters (K D T L S) are accepted on import and converted.
- A Game is always complete: moves through to the result and both players' names. An Elo left empty or entered as 0 means unrated, and unrated is shown as such.
- Board: chessground. Instant move feedback in the UI comes from chess.js; the C# backend is the authority on chess rules.
- Personal use only. There is no distribution, no other users, no accounts.

## Brand Commitments

None yet. The app has no name; a working name is fine.

## Evidence on Hand

No real game data in the repo yet. The Owner has their own games to import as PGN. Do not invent sample players, ratings or results presented as the Owner's.

## Product Principles

1. The board and the moves come first; everything else supports reviewing a game.
2. The Owner's own words stay theirs: engine verdicts are visible but never overwrite annotations.
3. Fast for the frequent path: entering a fresh game and seeing its analysis should take minutes, not a session.
4. Built for one experienced user: dense, keyboard-friendly, no onboarding ceremony.
