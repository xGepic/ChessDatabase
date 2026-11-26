namespace ChessDatabase.Rules;

public enum Color
{
    White,
    Black,
}

public enum PieceType
{
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6,
}

public static class ColorExtensions
{
    public static Color Opposite(this Color color) => color == Color.White ? Color.Black : Color.White;
}

public readonly record struct Piece(Color Color, PieceType Type)
{
    /// <summary>FEN letter: upper case for White, lower case for Black.</summary>
    public char ToFenChar()
    {
        var c = Type switch
        {
            PieceType.Pawn => 'p',
            PieceType.Knight => 'n',
            PieceType.Bishop => 'b',
            PieceType.Rook => 'r',
            PieceType.Queen => 'q',
            _ => 'k',
        };
        return Color == Color.White ? char.ToUpperInvariant(c) : c;
    }

    internal sbyte Code => (sbyte)(Color == Color.White ? (int)Type : -(int)Type);

    internal static Piece? FromCode(sbyte code) =>
        code == 0 ? null : new Piece(code > 0 ? Color.White : Color.Black, (PieceType)Math.Abs(code));
}

/// <summary>A board square, 0 = a1 … 7 = h1 … 63 = h8.</summary>
public readonly record struct Square
{
    public Square(int index)
    {
        if ((uint)index > 63) throw new ArgumentOutOfRangeException(nameof(index));
        Index = index;
    }

    public int Index { get; }

    /// <summary>0 = a-file … 7 = h-file.</summary>
    public int File => Index & 7;

    /// <summary>0 = first rank … 7 = eighth rank.</summary>
    public int Rank => Index >> 3;

    public static Square At(int file, int rank) => new(rank * 8 + file);

    public static Square Parse(string text) =>
        TryParse(text, out var square) ? square : throw new FormatException($"'{text}' is not a square.");

    public static bool TryParse(ReadOnlySpan<char> text, out Square square)
    {
        square = default;
        if (text.Length != 2 || text[0] < 'a' || text[0] > 'h' || text[1] < '1' || text[1] > '8') return false;
        square = At(text[0] - 'a', text[1] - '1');
        return true;
    }

    public override string ToString() => $"{(char)('a' + File)}{(char)('1' + Rank)}";
}

/// <summary>
/// A move from one square to another. Castling is the king's two-square move (e1g1), as in UCI.
/// </summary>
public readonly record struct Move(Square From, Square To, PieceType? Promotion = null)
{
    public string ToUci() => Promotion is { } p
        ? $"{From}{To}{char.ToLowerInvariant(new Piece(Color.Black, p).ToFenChar())}"
        : $"{From}{To}";

    public static bool TryParseUci(string? text, out Move move)
    {
        move = default;
        if (text is null || (text.Length != 4 && text.Length != 5)) return false;
        if (!Square.TryParse(text.AsSpan(0, 2), out var from) || !Square.TryParse(text.AsSpan(2, 2), out var to))
            return false;
        PieceType? promotion = null;
        if (text.Length == 5)
        {
            promotion = text[4] switch
            {
                'q' => PieceType.Queen,
                'r' => PieceType.Rook,
                'b' => PieceType.Bishop,
                'n' => PieceType.Knight,
                _ => null,
            };
            if (promotion is null) return false;
        }
        move = new Move(from, to, promotion);
        return true;
    }

    public override string ToString() => ToUci();
}

public sealed class IllegalMoveException(string message) : Exception(message);
