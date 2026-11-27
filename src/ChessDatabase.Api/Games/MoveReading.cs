using ChessDatabase.Rules;

namespace ChessDatabase.Api.Games;

/// <summary>Moves as the Owner types them or the UI sends them, read through Chess Rules.</summary>
public static class MoveReading
{
    /// <summary>
    /// Reads moves played one after another from a position into checked (UCI, SAN) pairs.
    /// Returns the first move that is not legal where it is played, or null when all are.
    /// </summary>
    public static string? ReadLine(Position position, IReadOnlyList<string?> moves, out List<(string Uci, string San)> line)
    {
        line = [];
        foreach (var text in moves)
        {
            if (!TryReadMove(position, text, out var move)) return text ?? "";
            line.Add((move.ToUci(), position.ToSan(move)));
            position = position.Play(move);
        }
        return null;
    }

    /// <summary>A move given as UCI or SAN (German piece letters too), if it is legal here.</summary>
    public static bool TryReadMove(Position position, string? text, out Move move)
    {
        text = text?.Trim();
        if (Move.TryParseUci(text, out move) && position.IsLegal(move)) return true;
        return position.TryParseSan(text, out move);
    }
}
