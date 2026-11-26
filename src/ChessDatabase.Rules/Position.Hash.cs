namespace ChessDatabase.Rules;

public sealed partial class Position
{
    /// <summary>
    /// Zobrist hash of the position: pieces, side to move, castling rights and a capturable
    /// en passant square. Move counters are not included, so transpositions hash equal.
    /// The keys come from a fixed-seed generator, so hashes are stable across runs and can be stored.
    /// </summary>
    public ulong Hash => _hash ??= ComputeHash();

    private ulong ComputeHash()
    {
        ulong hash = 0;
        for (var sq = 0; sq < 64; sq++)
        {
            var code = _board[sq];
            if (code == 0) continue;
            var pieceIndex = code > 0 ? code - 1 : 6 + (-code - 1);
            hash ^= ZobristKeys.Pieces[pieceIndex * 64 + sq];
        }

        if (SideToMove == Color.Black) hash ^= ZobristKeys.BlackToMove;
        hash ^= ZobristKeys.Castling[_castling];
        if (HasCapturableEnPassant()) hash ^= ZobristKeys.EnPassantFile[_enPassant & 7];
        return hash;
    }

    /// <summary>
    /// An en passant square only matters to the hash when a pawn of the side to move stands
    /// next to the double-pushed pawn, so it does not split otherwise identical positions.
    /// </summary>
    private bool HasCapturableEnPassant()
    {
        if (_enPassant < 0) return false;
        var sign = SideToMove == Color.White ? 1 : -1;
        var pawn = (sbyte)((int)PieceType.Pawn * sign);
        var captureRank = (_enPassant >> 3) - sign;
        var file = _enPassant & 7;
        return (file > 0 && _board[captureRank * 8 + file - 1] == pawn)
               || (file < 7 && _board[captureRank * 8 + file + 1] == pawn);
    }

    private static class ZobristKeys
    {
        public static readonly ulong[] Pieces = new ulong[12 * 64];
        public static readonly ulong[] Castling = new ulong[16];
        public static readonly ulong[] EnPassantFile = new ulong[8];
        public static readonly ulong BlackToMove;

        static ZobristKeys()
        {
            // SplitMix64 with a fixed seed: our own generator, so the keys never change with the runtime.
            var state = 0x9E3779B97F4A7C15UL ^ 0xC0FFEE_2026UL;
            for (var i = 0; i < Pieces.Length; i++) Pieces[i] = Next(ref state);
            var castlingBits = new ulong[4];
            for (var i = 0; i < 4; i++) castlingBits[i] = Next(ref state);
            for (var rights = 0; rights < 16; rights++)
                for (var bit = 0; bit < 4; bit++)
                    if ((rights & (1 << bit)) != 0) Castling[rights] ^= castlingBits[bit];
            for (var i = 0; i < 8; i++) EnPassantFile[i] = Next(ref state);
            BlackToMove = Next(ref state);
        }

        private static ulong Next(ref ulong state)
        {
            var z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
