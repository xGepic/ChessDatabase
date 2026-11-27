namespace ChessDatabase.Rules;

/// <summary>
/// Moves played one after another from a position, replayed once: every position on the way and
/// each move in UCI and SAN. A Game's main line or a Repertoire line is one.
/// </summary>
public sealed class Line
{
    private readonly List<Position> _positions;
    private readonly List<Move> _moves;
    private IReadOnlyList<string>? _uci;
    private IReadOnlyList<string>? _san;

    private Line(Position start)
    {
        _positions = [start];
        _moves = [];
    }

    /// <summary>The start first, then the position after each move: one more than there are moves.</summary>
    public IReadOnlyList<Position> Positions => _positions;

    public IReadOnlyList<Move> Moves => _moves;

    public IReadOnlyList<string> Uci => _uci ??= [.. _moves.Select(m => m.ToUci())];

    /// <summary>English piece letters, as written everywhere in the app. Worked out when first asked for.</summary>
    public IReadOnlyList<string> San => _san ??= [.. _moves.Select((move, i) => _positions[i].ToSan(move))];

    public int Count => _moves.Count;

    /// <summary>The position after the last move.</summary>
    public Position End => _positions[^1];

    /// <summary>
    /// Replays stored moves (UCI) from the starting position. Stored moves were checked when they
    /// were saved, so one that is not UCI (<see cref="FormatException"/>) or not legal
    /// (<see cref="IllegalMoveException"/>) is a fault in the stored data.
    /// </summary>
    public static Line FromUci(IEnumerable<string> moves)
    {
        var line = new Line(Position.Initial);
        foreach (var uci in moves) line.Add(Move.ParseUci(uci)); // Play refuses an illegal move
        return line;
    }

    /// <summary>
    /// Reads moves as the Owner types them or the UI sends them (UCI or SAN, German piece letters
    /// too) from a position. Null when one is not legal where it is played; <paramref name="illegal"/>
    /// is then the first such move as it was given.
    /// </summary>
    public static Line? Read(Position start, IEnumerable<string?> moves, out string? illegal)
    {
        var line = new Line(start);
        foreach (var text in moves)
        {
            if (!line.End.TryReadMove(text, out var move))
            {
                illegal = text ?? "";
                return null;
            }
            line.Add(move);
        }
        illegal = null;
        return line;
    }

    private void Add(Move move)
    {
        _positions.Add(End.Play(move));
        _moves.Add(move);
    }
}
