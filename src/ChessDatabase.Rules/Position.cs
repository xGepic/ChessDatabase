using System.Text;

namespace ChessDatabase.Rules;

/// <summary>
/// An immutable chess position: the single source of truth for chess rules (ADR 0002).
/// Board squares hold signed piece codes: positive for White, negative for Black, 0 for empty.
/// </summary>
public sealed partial class Position
{
    private const int WhiteKingside = 1, WhiteQueenside = 2, BlackKingside = 4, BlackQueenside = 8;

    private readonly sbyte[] _board;
    private readonly int _castling;
    private readonly int _enPassant; // target square index, or -1
    private IReadOnlyList<Move>? _legalMoves;
    private ulong? _hash;

    private Position(sbyte[] board, Color sideToMove, int castling, int enPassant, int halfmoveClock, int fullmoveNumber)
    {
        _board = board;
        SideToMove = sideToMove;
        _castling = castling;
        _enPassant = enPassant;
        HalfmoveClock = halfmoveClock;
        FullmoveNumber = fullmoveNumber;
    }

    public const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public static Position Initial { get; } = FromFen(InitialFen);

    public Color SideToMove { get; }
    public int HalfmoveClock { get; }
    public int FullmoveNumber { get; }

    public Piece? this[Square square] => Piece.FromCode(_board[square.Index]);

    public Square? EnPassantSquare => _enPassant < 0 ? null : new Square(_enPassant);

    /// <summary>The side to move is in check.</summary>
    public bool IsCheck => IsAttacked(_board, KingSquare(_board, SideToMove), SideToMove.Opposite());

    public bool IsCheckmate => IsCheck && LegalMoves().Count == 0;

    public bool IsStalemate => !IsCheck && LegalMoves().Count == 0;

    public IReadOnlyList<Move> LegalMoves() => _legalMoves ??= GenerateLegalMoves();

    public bool IsLegal(Move move)
    {
        foreach (var m in LegalMoves())
            if (m == move) return true;
        return false;
    }

    /// <summary>Plays a legal move and returns the resulting position.</summary>
    public Position Play(Move move)
    {
        if (!IsLegal(move)) throw new IllegalMoveException($"{move.ToUci()} is not legal in {ToFen()}.");
        return PlayUnchecked(move);
    }

    // ---------------------------------------------------------------- move generation

    private static readonly (int df, int dr)[] KnightSteps =
        [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int df, int dr)[] KingSteps =
        [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];
    private static readonly (int df, int dr)[] RookDirections = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int df, int dr)[] BishopDirections = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    private static readonly int[][] KnightTargets = BuildStepTable(KnightSteps);
    private static readonly int[][] KingTargets = BuildStepTable(KingSteps);
    private static readonly int[][][] RookRays = BuildRayTable(RookDirections);
    private static readonly int[][][] BishopRays = BuildRayTable(BishopDirections);

    private static int[][] BuildStepTable((int df, int dr)[] steps)
    {
        var table = new int[64][];
        for (var sq = 0; sq < 64; sq++)
        {
            var targets = new List<int>();
            foreach (var (df, dr) in steps)
            {
                int f = (sq & 7) + df, r = (sq >> 3) + dr;
                if (f is >= 0 and < 8 && r is >= 0 and < 8) targets.Add(r * 8 + f);
            }
            table[sq] = [.. targets];
        }
        return table;
    }

    private static int[][][] BuildRayTable((int df, int dr)[] directions)
    {
        var table = new int[64][][];
        for (var sq = 0; sq < 64; sq++)
        {
            table[sq] = new int[directions.Length][];
            for (var d = 0; d < directions.Length; d++)
            {
                var ray = new List<int>();
                int f = (sq & 7) + directions[d].df, r = (sq >> 3) + directions[d].dr;
                while (f is >= 0 and < 8 && r is >= 0 and < 8)
                {
                    ray.Add(r * 8 + f);
                    f += directions[d].df;
                    r += directions[d].dr;
                }
                table[sq][d] = [.. ray];
            }
        }
        return table;
    }

    private List<Move> GenerateLegalMoves()
    {
        var pseudo = new List<Move>(48);
        GeneratePseudoLegal(pseudo);
        var legal = new List<Move>(pseudo.Count);
        Span<sbyte> scratch = stackalloc sbyte[64];
        var us = SideToMove;
        foreach (var move in pseudo)
        {
            _board.CopyTo(scratch);
            ApplyToBoard(scratch, move);
            if (!IsAttacked(scratch, KingSquare(scratch, us), us.Opposite())) legal.Add(move);
        }
        return legal;
    }

    private void GeneratePseudoLegal(List<Move> moves)
    {
        var sign = SideToMove == Color.White ? 1 : -1;
        for (var from = 0; from < 64; from++)
        {
            var code = _board[from] * sign;
            if (code <= 0) continue;
            switch ((PieceType)code)
            {
                case PieceType.Pawn: GeneratePawnMoves(moves, from, sign); break;
                case PieceType.Knight: GenerateSteps(moves, from, KnightTargets[from], sign); break;
                case PieceType.Bishop: GenerateSlides(moves, from, BishopRays[from], sign); break;
                case PieceType.Rook: GenerateSlides(moves, from, RookRays[from], sign); break;
                case PieceType.Queen:
                    GenerateSlides(moves, from, BishopRays[from], sign);
                    GenerateSlides(moves, from, RookRays[from], sign);
                    break;
                case PieceType.King:
                    GenerateSteps(moves, from, KingTargets[from], sign);
                    GenerateCastling(moves, from);
                    break;
            }
        }
    }

    private void GenerateSteps(List<Move> moves, int from, int[] targets, int sign)
    {
        foreach (var to in targets)
            if (_board[to] * sign <= 0) moves.Add(new Move(new Square(from), new Square(to)));
    }

    private void GenerateSlides(List<Move> moves, int from, int[][] rays, int sign)
    {
        foreach (var ray in rays)
        {
            foreach (var to in ray)
            {
                var target = _board[to] * sign;
                if (target > 0) break;
                moves.Add(new Move(new Square(from), new Square(to)));
                if (target < 0) break;
            }
        }
    }

    private void GeneratePawnMoves(List<Move> moves, int from, int sign)
    {
        int file = from & 7, rank = from >> 3;
        var forward = 8 * sign;
        var startRank = sign > 0 ? 1 : 6;
        var lastRank = sign > 0 ? 7 : 0;

        var one = from + forward;
        if (_board[one] == 0)
        {
            AddPawnMove(moves, from, one, lastRank);
            var two = one + forward;
            if (rank == startRank && _board[two] == 0) moves.Add(new Move(new Square(from), new Square(two)));
        }

        foreach (var df in (ReadOnlySpan<int>)[-1, 1])
        {
            var f = file + df;
            if (f is < 0 or > 7) continue;
            var to = one + df;
            if (_board[to] * sign < 0 || to == _enPassant) AddPawnMove(moves, from, to, lastRank);
        }
    }

    private static void AddPawnMove(List<Move> moves, int from, int to, int lastRank)
    {
        if (to >> 3 == lastRank)
        {
            foreach (var p in (ReadOnlySpan<PieceType>)[PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight])
                moves.Add(new Move(new Square(from), new Square(to), p));
        }
        else
        {
            moves.Add(new Move(new Square(from), new Square(to)));
        }
    }

    private void GenerateCastling(List<Move> moves, int from)
    {
        var white = SideToMove == Color.White;
        var home = white ? 4 : 60;
        if (from != home) return;
        var them = SideToMove.Opposite();
        int kingside = white ? WhiteKingside : BlackKingside, queenside = white ? WhiteQueenside : BlackQueenside;
        var rook = (sbyte)(white ? (int)PieceType.Rook : -(int)PieceType.Rook);

        if ((_castling & kingside) != 0 && _board[home + 3] == rook
            && _board[home + 1] == 0 && _board[home + 2] == 0
            && !IsAttacked(_board, home, them) && !IsAttacked(_board, home + 1, them) && !IsAttacked(_board, home + 2, them))
            moves.Add(new Move(new Square(home), new Square(home + 2)));

        if ((_castling & queenside) != 0 && _board[home - 4] == rook
            && _board[home - 1] == 0 && _board[home - 2] == 0 && _board[home - 3] == 0
            && !IsAttacked(_board, home, them) && !IsAttacked(_board, home - 1, them) && !IsAttacked(_board, home - 2, them))
            moves.Add(new Move(new Square(home), new Square(home - 2)));
    }

    // ---------------------------------------------------------------- making moves

    /// <summary>Moves pieces on a board for a (pseudo-)legal move, handling castling, en passant and promotion.</summary>
    private void ApplyToBoard(Span<sbyte> board, Move move)
    {
        int from = move.From.Index, to = move.To.Index;
        var piece = board[from];
        var type = (PieceType)Math.Abs(piece);
        var sign = piece > 0 ? 1 : -1;

        if (type == PieceType.Pawn && to == _enPassant) board[to - 8 * sign] = 0;

        if (type == PieceType.King && Math.Abs(to - from) == 2)
        {
            int rookFrom = to > from ? from + 3 : from - 4, rookTo = to > from ? from + 1 : from - 1;
            board[rookTo] = board[rookFrom];
            board[rookFrom] = 0;
        }

        board[to] = move.Promotion is { } promotion ? (sbyte)((int)promotion * sign) : piece;
        board[from] = 0;
    }

    private Position PlayUnchecked(Move move)
    {
        var board = (sbyte[])_board.Clone();
        int from = move.From.Index, to = move.To.Index;
        var type = (PieceType)Math.Abs(_board[from]);
        var isCapture = _board[to] != 0 || (type == PieceType.Pawn && to == _enPassant);
        ApplyToBoard(board, move);

        var castling = _castling & CastlingMaskAfterTouching(from) & CastlingMaskAfterTouching(to);

        var enPassant = -1;
        if (type == PieceType.Pawn && Math.Abs(to - from) == 16)
        {
            // Only record the en passant square when an enemy pawn could actually capture there.
            var target = (from + to) / 2;
            var enemyPawn = (sbyte)(SideToMove == Color.White ? -(int)PieceType.Pawn : (int)PieceType.Pawn);
            var file = to & 7;
            if ((file > 0 && board[to - 1] == enemyPawn) || (file < 7 && board[to + 1] == enemyPawn)) enPassant = target;
        }

        var halfmove = type == PieceType.Pawn || isCapture ? 0 : HalfmoveClock + 1;
        var fullmove = SideToMove == Color.Black ? FullmoveNumber + 1 : FullmoveNumber;
        return new Position(board, SideToMove.Opposite(), castling, enPassant, halfmove, fullmove);
    }

    private static int CastlingMaskAfterTouching(int square) => square switch
    {
        0 => ~WhiteQueenside,
        4 => ~(WhiteKingside | WhiteQueenside),
        7 => ~WhiteKingside,
        56 => ~BlackQueenside,
        60 => ~(BlackKingside | BlackQueenside),
        63 => ~BlackKingside,
        _ => ~0,
    };

    // ---------------------------------------------------------------- attacks

    private static int KingSquare(ReadOnlySpan<sbyte> board, Color color)
    {
        var king = (sbyte)(color == Color.White ? (int)PieceType.King : -(int)PieceType.King);
        for (var sq = 0; sq < 64; sq++)
            if (board[sq] == king) return sq;
        throw new InvalidOperationException($"No {color} king on the board.");
    }

    private static bool IsAttacked(ReadOnlySpan<sbyte> board, int square, Color by)
    {
        var sign = by == Color.White ? 1 : -1;

        // Pawns attack diagonally forward, so look one rank "behind" the square from the attacker's view.
        int file = square & 7, pawnRank = (square >> 3) - sign;
        if (pawnRank is >= 0 and < 8)
        {
            var pawn = (sbyte)((int)PieceType.Pawn * sign);
            if (file > 0 && board[pawnRank * 8 + file - 1] == pawn) return true;
            if (file < 7 && board[pawnRank * 8 + file + 1] == pawn) return true;
        }

        var knight = (sbyte)((int)PieceType.Knight * sign);
        foreach (var sq in KnightTargets[square])
            if (board[sq] == knight) return true;

        var king = (sbyte)((int)PieceType.King * sign);
        foreach (var sq in KingTargets[square])
            if (board[sq] == king) return true;

        var queen = (sbyte)((int)PieceType.Queen * sign);
        var rook = (sbyte)((int)PieceType.Rook * sign);
        var bishop = (sbyte)((int)PieceType.Bishop * sign);
        if (RayHits(board, RookRays[square], rook, queen)) return true;
        if (RayHits(board, BishopRays[square], bishop, queen)) return true;
        return false;
    }

    private static bool RayHits(ReadOnlySpan<sbyte> board, int[][] rays, sbyte slider, sbyte queen)
    {
        foreach (var ray in rays)
        {
            foreach (var sq in ray)
            {
                var code = board[sq];
                if (code == 0) continue;
                if (code == slider || code == queen) return true;
                break;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- FEN

    public static Position FromFen(string fen)
    {
        ArgumentNullException.ThrowIfNull(fen);
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 4 or > 6) throw new FormatException($"'{fen}' is not a valid FEN.");

        var board = new sbyte[64];
        var ranks = parts[0].Split('/');
        if (ranks.Length != 8) throw new FormatException($"'{fen}' does not have 8 ranks.");
        for (var i = 0; i < 8; i++)
        {
            var rank = 7 - i;
            var file = 0;
            foreach (var c in ranks[i])
            {
                if (c is >= '1' and <= '8')
                {
                    file += c - '0';
                    continue;
                }
                if (file > 7) throw new FormatException($"Rank {rank + 1} in '{fen}' is too long.");
                var type = char.ToLowerInvariant(c) switch
                {
                    'p' => PieceType.Pawn,
                    'n' => PieceType.Knight,
                    'b' => PieceType.Bishop,
                    'r' => PieceType.Rook,
                    'q' => PieceType.Queen,
                    'k' => PieceType.King,
                    _ => throw new FormatException($"'{c}' is not a piece in '{fen}'."),
                };
                board[rank * 8 + file] = new Piece(char.IsUpper(c) ? Color.White : Color.Black, type).Code;
                file++;
            }
            if (file != 8) throw new FormatException($"Rank {rank + 1} in '{fen}' does not have 8 files.");
        }

        var side = parts[1] switch
        {
            "w" => Color.White,
            "b" => Color.Black,
            _ => throw new FormatException($"'{parts[1]}' is not a side to move."),
        };

        var castling = 0;
        if (parts[2] != "-")
        {
            foreach (var c in parts[2])
            {
                castling |= c switch
                {
                    'K' => WhiteKingside,
                    'Q' => WhiteQueenside,
                    'k' => BlackKingside,
                    'q' => BlackQueenside,
                    _ => throw new FormatException($"'{parts[2]}' is not a castling field."),
                };
            }
        }

        var enPassant = -1;
        if (parts[3] != "-")
        {
            if (!Square.TryParse(parts[3], out var ep)) throw new FormatException($"'{parts[3]}' is not an en passant square.");
            enPassant = ep.Index;
        }

        var halfmove = parts.Length > 4 ? ParseCounter(parts[4]) : 0;
        var fullmove = parts.Length > 5 ? ParseCounter(parts[5]) : 1;

        int whiteKings = board.Count(c => c == (sbyte)PieceType.King), blackKings = board.Count(c => c == -(sbyte)PieceType.King);
        if (whiteKings != 1 || blackKings != 1) throw new FormatException($"'{fen}' must have exactly one king per side.");

        return new Position(board, side, castling, enPassant, halfmove, fullmove);

        int ParseCounter(string s) =>
            int.TryParse(s, out var n) && n >= 0 ? n : throw new FormatException($"'{s}' is not a move counter.");
    }

    public string ToFen()
    {
        var sb = new StringBuilder(90);
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                var piece = Piece.FromCode(_board[rank * 8 + file]);
                if (piece is null)
                {
                    empty++;
                    continue;
                }
                if (empty > 0) sb.Append(empty);
                empty = 0;
                sb.Append(piece.Value.ToFenChar());
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }

        sb.Append(SideToMove == Color.White ? " w " : " b ");
        if (_castling == 0) sb.Append('-');
        if ((_castling & WhiteKingside) != 0) sb.Append('K');
        if ((_castling & WhiteQueenside) != 0) sb.Append('Q');
        if ((_castling & BlackKingside) != 0) sb.Append('k');
        if ((_castling & BlackQueenside) != 0) sb.Append('q');
        sb.Append(' ').Append(_enPassant < 0 ? "-" : new Square(_enPassant).ToString());
        sb.Append(' ').Append(HalfmoveClock).Append(' ').Append(FullmoveNumber);
        return sb.ToString();
    }

    public override string ToString() => ToFen();
}
