# C# backend is the single source of truth for chess rules

Both tiers need chess rules: the backend for PGN import, legality checks and position hashing (Position Search, Opening Tree), and the UI for instant feedback when a piece is dragged. C# owns the rules and validates everything that gets saved. The Angular UI uses chess.js only to stay responsive and never decides what is stored. This avoids two diverging rule implementations each believing it is authoritative. The C# move generator, whether written by us or taken from a library, must pass the standard perft move-count tests.
