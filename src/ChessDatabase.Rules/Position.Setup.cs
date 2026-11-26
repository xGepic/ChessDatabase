namespace ChessDatabase.Rules;

public sealed partial class Position
{
    /// <summary>
    /// Why a set-up position (from a FEN or the position editor) could not occur in a game, or
    /// null when it can be used. FEN parsing already ensures one king per side.
    /// </summary>
    public string? SetupProblem()
    {
        for (var file = 0; file < 8; file++)
            if (Math.Abs(_board[file]) == (int)PieceType.Pawn || Math.Abs(_board[56 + file]) == (int)PieceType.Pawn)
                return "Pawns can't stand on the first or the last rank.";

        var them = SideToMove.Opposite();
        if (IsAttacked(_board, KingSquare(_board, them), SideToMove))
            return $"{them} is in check, but it is {SideToMove}'s turn.";

        if (!CastlingPiecesInPlace(Color.White, WhiteKingside, WhiteQueenside) || !CastlingPiecesInPlace(Color.Black, BlackKingside, BlackQueenside))
            return "Castling needs the king and that rook on their starting squares.";

        if (_enPassant >= 0)
        {
            var (rank, pawnRank) = SideToMove == Color.White ? (5, 4) : (2, 3);
            var file = _enPassant & 7;
            var pawn = (sbyte)((int)PieceType.Pawn * (SideToMove == Color.White ? -1 : 1));
            if (_enPassant >> 3 != rank || _board[pawnRank * 8 + file] != pawn || _board[_enPassant] != 0)
                return "The en passant square doesn't follow a two-square pawn move.";
        }
        return null;
    }

    private bool CastlingPiecesInPlace(Color color, int kingside, int queenside)
    {
        var sign = color == Color.White ? 1 : -1;
        var home = color == Color.White ? 4 : 60;
        var king = (sbyte)((int)PieceType.King * sign);
        var rook = (sbyte)((int)PieceType.Rook * sign);
        if ((_castling & (kingside | queenside)) != 0 && _board[home] != king) return false;
        if ((_castling & kingside) != 0 && _board[home + 3] != rook) return false;
        if ((_castling & queenside) != 0 && _board[home - 4] != rook) return false;
        return true;
    }
}
