using ChessDatabase.Rules;

namespace ChessDatabase.Api.Engines;

/// <summary>An Engine evaluation: centipawns, or moves to mate (negative: being mated). Mate 0 means mated now.</summary>
public readonly record struct EngineScore(int? Cp, int? Mate)
{
    /// <summary>
    /// UCI scores are from the side to move; the app shows them from White's side so a positive
    /// number always favours White.
    /// </summary>
    public EngineScore ForWhite(Color sideToMove) =>
        sideToMove == Color.White ? this : new EngineScore(-Cp, Mate is { } m ? -m : null);
}

/// <summary>One "info" line of a UCI engine that carries a score and a line (others are ignored).</summary>
public sealed record UciInfo(int Depth, int MultiPv, EngineScore Score, IReadOnlyList<string> Pv)
{
    /// <summary>
    /// Reads an info line with a score and a principal variation. Lines with only a lower or upper
    /// bound are skipped: their score is not the Engine's verdict yet.
    /// </summary>
    public static bool TryParse(string line, out UciInfo info)
    {
        info = null!;
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words[0] != "info") return false;

        int depth = 0, multiPv = 1;
        int? cp = null, mate = null;
        List<string>? pv = null;
        for (var i = 1; i < words.Length; i++)
        {
            switch (words[i])
            {
                case "depth" when i + 1 < words.Length: int.TryParse(words[++i], out depth); break;
                case "multipv" when i + 1 < words.Length: int.TryParse(words[++i], out multiPv); break;
                case "score" when i + 2 < words.Length:
                    if (!int.TryParse(words[i + 2], out var value)) return false;
                    if (words[i + 1] == "cp") cp = value;
                    else if (words[i + 1] == "mate") mate = value;
                    i += 2;
                    break;
                case "lowerbound" or "upperbound": return false;
                case "string": return false;
                case "pv": pv = [.. words[(i + 1)..]]; i = words.Length; break;
            }
        }
        if (cp is null && mate is null) return false;
        info = new UciInfo(depth, multiPv, new EngineScore(cp, mate), pv ?? []);
        return true;
    }

    /// <summary>
    /// The line as legal moves from the position, in UCI and SAN. An Engine line is cut at the first
    /// move Chess Rules don't accept, so nothing illegal ever reaches the Owner.
    /// </summary>
    public (List<string> Uci, List<string> San) LegalLine(Position position)
    {
        List<string> uci = [], san = [];
        foreach (var text in Pv)
        {
            if (!Move.TryParseUci(text, out var move) || !position.IsLegal(move)) break;
            uci.Add(move.ToUci());
            san.Add(position.ToSan(move));
            position = position.Play(move);
        }
        return (uci, san);
    }
}
