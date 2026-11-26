using System.Text;
using System.Text.RegularExpressions;

namespace ChessDatabase.Rules;

public sealed partial class Position
{
    /// <summary>
    /// Formats a legal move in Standard Algebraic Notation with English piece letters,
    /// minimal disambiguation and a check (+) or mate (#) suffix.
    /// </summary>
    public string ToSan(Move move)
    {
        if (!IsLegal(move)) throw new IllegalMoveException($"{move.ToUci()} is not legal in {ToFen()}.");

        var piece = this[move.From]!.Value;
        var sb = new StringBuilder(8);

        if (piece.Type == PieceType.King && Math.Abs(move.To.Index - move.From.Index) == 2)
        {
            sb.Append(move.To.Index > move.From.Index ? "O-O" : "O-O-O");
        }
        else
        {
            var isCapture = this[move.To] is not null || (piece.Type == PieceType.Pawn && move.To.Index == _enPassant);
            if (piece.Type == PieceType.Pawn)
            {
                if (isCapture) sb.Append((char)('a' + move.From.File));
            }
            else
            {
                sb.Append(char.ToUpperInvariant(piece.ToFenChar()));
                AppendDisambiguation(sb, move, piece.Type);
            }

            if (isCapture) sb.Append('x');
            sb.Append(move.To);
            if (move.Promotion is { } promotion)
                sb.Append('=').Append(char.ToUpperInvariant(new Piece(Color.White, promotion).ToFenChar()));
        }

        var after = PlayUnchecked(move);
        if (after.IsCheckmate) sb.Append('#');
        else if (after.IsCheck) sb.Append('+');
        return sb.ToString();
    }

    private void AppendDisambiguation(StringBuilder sb, Move move, PieceType type)
    {
        var rivals = LegalMoves()
            .Where(m => m.To == move.To && m.From != move.From && this[m.From]!.Value.Type == type)
            .ToList();
        if (rivals.Count == 0) return;

        var fileUnique = rivals.All(m => m.From.File != move.From.File);
        var rankUnique = rivals.All(m => m.From.Rank != move.From.Rank);
        if (fileUnique) sb.Append((char)('a' + move.From.File));
        else if (rankUnique) sb.Append((char)('1' + move.From.Rank));
        else sb.Append(move.From);
    }

    /// <summary>
    /// Parses SAN. Accepts English (K Q R B N) and German (K D T L S) piece letters, castling
    /// written with O or 0, and ignores check, mate and annotation suffixes such as "+", "!?".
    /// </summary>
    public Move ParseSan(string san) =>
        TryParseSan(san, out var move)
            ? move
            : throw new IllegalMoveException($"'{san}' is not a legal move in {ToFen()}.");

    public bool TryParseSan(string? san, out Move move)
    {
        move = default;
        if (string.IsNullOrWhiteSpace(san)) return false;
        var text = san.Trim().TrimEnd('+', '#', '!', '?');

        if (text is "O-O" or "0-0" or "O-O-O" or "0-0-0")
        {
            var kingside = text.Length == 3;
            var home = SideToMove == Color.White ? 4 : 60;
            return TryFindUnique(m => m.From.Index == home && m.To.Index == home + (kingside ? 2 : -2)
                                      && _board[home] * (SideToMove == Color.White ? 1 : -1) == (int)PieceType.King, out move);
        }

        var match = SanPattern().Match(text);
        if (!match.Success) return false;

        var type = match.Groups["piece"].Success ? PieceFromLetter(match.Groups["piece"].Value[0]) : PieceType.Pawn;
        var to = Square.Parse(match.Groups["to"].Value);
        int? fromFile = match.Groups["file"].Success ? match.Groups["file"].Value[0] - 'a' : null;
        int? fromRank = match.Groups["rank"].Success ? match.Groups["rank"].Value[0] - '1' : null;
        PieceType? promotion = match.Groups["promo"].Success ? PieceFromLetter(match.Groups["promo"].Value[0]) : null;
        if (promotion is PieceType.King or PieceType.Pawn) return false;

        var sign = SideToMove == Color.White ? 1 : -1;
        return TryFindUnique(m =>
            m.To == to
            && _board[m.From.Index] * sign == (int)type
            && (fromFile is null || m.From.File == fromFile)
            && (fromRank is null || m.From.Rank == fromRank)
            && m.Promotion == promotion, out move);
    }

    private bool TryFindUnique(Func<Move, bool> predicate, out Move move)
    {
        move = default;
        var found = 0;
        foreach (var m in LegalMoves())
        {
            if (!predicate(m)) continue;
            move = m;
            found++;
        }
        return found == 1;
    }

    private static PieceType PieceFromLetter(char letter) => letter switch
    {
        'K' => PieceType.King,
        'Q' or 'D' => PieceType.Queen,
        'R' or 'T' => PieceType.Rook,
        'B' or 'L' => PieceType.Bishop,
        'N' or 'S' => PieceType.Knight,
        _ => throw new ArgumentOutOfRangeException(nameof(letter)),
    };

    [GeneratedRegex("^(?<piece>[KQRBNDTLS])?(?<file>[a-h])?(?<rank>[1-8])?[x:]?(?<to>[a-h][1-8])(=?(?<promo>[QRBNDTLS]))?$")]
    private static partial Regex SanPattern();
}
