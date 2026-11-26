# Glossary

## Games and players

**Game**
A chess game the Owner played, mostly over the board, recorded with its moves, Players, ratings and result. Games are entered by hand or imported from PGN. A Game is always complete: every move through to the result and both players' names, with each side either rated (an Elo) or unrated. Event, site, date and the other details are optional. A game the Owner only watched is not a Game.

**Collection**
The single set of all the Owner's Games. There is exactly one.

**Owner**
The single person who uses the app. The Owner is always one of the two players in every Game, so each Game has an "Owner's colour".

**Player**
A person the Owner has played against, kept as one record so that the same opponent across many Games is recognised as the same person. Basis for head-to-head history and player dossiers.

**Alias**
An alternative spelling of the Owner's name, such as "Mustermann M.". Used to recognise which side was the Owner when Games are imported.

**Opponent**
The Player on the other side of the board from the Owner in a given Game.

**Event**
The tournament, league, match or occasion a Game was played in.

**Tag**
A free-form label the Owner attaches to Games for grouping, such as "club league" or "blitz".

**ECO code**
The standard classification code of an opening (A00–E99), derived from a Game's moves.

**Elo** (rating)
A Player's rating at the time a particular Game was played. It belongs to the Game, not to the Player, because ratings change over time. A side can be **unrated**: this is shown as such and left out of rating statistics such as average opponent rating. An Elo left empty or entered as 0 means unrated.

**PGN**
The standard text format for chess games. Used to bring Games in and out of the app.

## Moves and commentary

**Variation**
An alternative sequence of moves branching off the moves actually played. Variations can contain further Variations.

**Annotation**
Commentary attached to a move or position: a text comment or a symbol such as `!`, `?` or `±`.

## Analysis

**Engine**
An external chess program (Stockfish by default) that evaluates positions and suggests the best moves.

**Interactive Analysis**
The Owner steps through a Game while the Engine continuously shows its evaluation and best lines for the current position.

**Game Analysis**
An unattended pass in which the Engine evaluates every move of a Game and flags blunders, mistakes and inaccuracies.

**Engine Assessment**
The Engine's verdict on a Game: evaluations, best moves and mistake classifications. It is kept separate from the Owner's own Annotations and becomes an Annotation only when the Owner explicitly copies it into the Game.

**Move Classification**
The label Game Analysis gives a move, based on how much winning chance it gave away: inaccuracy (about 10%), mistake (about 20%) or blunder (about 30% or more).

**Accuracy**
A per-player percentage summarising how closely their moves in a Game matched the Engine's choices.

## Research

**Opening Tree**
Move statistics for the current position. Its "My games" source draws on every Game in the Collection. Its "Masters" source draws on an online database of master games.

**Master Game**
A game between strong players, shown in the Opening Tree's Masters source. Not a Game: the Owner did not play it, and it is never stored in the Collection.

**Position Search**
Finding every Game that reached a given position, regardless of the move order that led there.

**Tablebase**
A solved database of endgames with few pieces, giving the exact result and best move. Looked up online.

**Dossier**
The summary page for a Player: head-to-head score against the Owner, rating history, the openings they played and the Games against them.

## Training

**Repertoire**
The opening lines the Owner intends to play, one for White and one for Black, stored as trees of moves.

**Deviation**
The first move in a Game where either side left the Owner's Repertoire.

**Repertoire Training**
Drilling Repertoire lines with spaced repetition.

**Mistake Training**
Replaying positions from the Owner's own Games where Game Analysis found a mistake or blunder, as puzzles to solve.
