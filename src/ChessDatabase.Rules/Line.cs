namespace ChessDatabase.Rules;

/// <summary>
/// Moves played one after another from a position, replayed once: every position on the way and
/// each move in UCI and SAN. A Game's main line, a Repertoire line or an Engine line is one.
/// </summary>
public sealed class Line
{
    private Line(List<Position> positions, List<Move> moves, List<string> san)
    {
        Positions = positions;
        Moves = moves;
        San = san;
        Uci = [.. moves.Select(m => m.ToUci())];
    }

    /// <summary>The start first, then the position after each move: one more than there are moves.</summary>
    public IReadOnlyList<Position> Positions { get; }

    public IReadOnlyList<Move> Moves { get; }

    public IReadOnlyList<string> Uci { get; }

    /// <summary>English piece letters, as written everywhere in the app.</summary>
    public IReadOnlyList<string> San { get; }

    public int Count => Moves.Count;

    public Position Start => Positions[0];

    /// <summary>The position after the last move.</summary>
    public Position End => Positions[^1];

    /// <summary>
    /// Replays stored moves (UCI) from the starting position. Stored moves were checked when they
    /// were saved, so one that is not UCI (<see cref="FormatException"/>) or not legal
    /// (<see cref="IllegalMoveException"/>) is a fault in the stored data.
    /// </summary>
    public static Line FromUci(IEnumerable<string> moves)
    {
        var positions = new List<Position> { Position.Initial };
        var played = new List<Move>();
        var san = new List<string>();
        foreach (var uci in moves)
        {
            var move = Move.ParseUci(uci);
            var position = positions[^1];
            if (!position.IsLegal(move)) throw new IllegalMoveException($"Stored move '{uci}' is not legal in {position.ToFen()}.");
            san.Add(position.ToSan(move));
            played.Add(move);
            positions.Add(position.Play(move));
        }
        return new Line(positions, played, san);
    }

    /// <summary>
    /// Reads moves as the Owner types them or the UI sends them (UCI or SAN, German piece letters
    /// too) from a position. Null when one is not legal where it is played; <paramref name="illegal"/>
    /// is then the first such move as it was given.
    /// </summary>
    public static Line? Read(Position start, IEnumerable<string?> moves, out string? illegal)
    {
        var positions = new List<Position> { start };
        var played = new List<Move>();
        var san = new List<string>();
        foreach (var text in moves)
        {
            var position = positions[^1];
            if (!position.TryReadMove(text, out var move))
            {
                illegal = text ?? "";
                return null;
            }
            san.Add(position.ToSan(move));
            played.Add(move);
            positions.Add(position.Play(move));
        }
        illegal = null;
        return new Line(positions, played, san);
    }
}
