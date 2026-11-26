using ChessDatabase.Rules;

namespace ChessDatabase.Rules.Tests;

/// <summary>
/// Perft counts leaf nodes of the legal move tree. Expected values are the published ones from
/// https://www.chessprogramming.org/Perft_Results, the established way to catch move generator bugs.
/// </summary>
public class PerftTests
{
    public const string Initial = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    public const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
    public const string Position3 = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";
    public const string Position4 = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1";
    public const string Position4Mirrored = "r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1";
    public const string Position5 = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";
    public const string Position6 = "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10";

    public static readonly string[] StandardPositions =
        [Initial, Kiwipete, Position3, Position4, Position4Mirrored, Position5, Position6];

    [Theory]
    [InlineData(Initial, 1, 20)]
    [InlineData(Initial, 2, 400)]
    [InlineData(Initial, 3, 8_902)]
    [InlineData(Initial, 4, 197_281)]
    [InlineData(Initial, 5, 4_865_609)]
    [InlineData(Kiwipete, 1, 48)]
    [InlineData(Kiwipete, 2, 2_039)]
    [InlineData(Kiwipete, 3, 97_862)]
    [InlineData(Kiwipete, 4, 4_085_603)]
    [InlineData(Position3, 1, 14)]
    [InlineData(Position3, 2, 191)]
    [InlineData(Position3, 3, 2_812)]
    [InlineData(Position3, 4, 43_238)]
    [InlineData(Position3, 5, 674_624)]
    [InlineData(Position4, 1, 6)]
    [InlineData(Position4, 2, 264)]
    [InlineData(Position4, 3, 9_467)]
    [InlineData(Position4, 4, 422_333)]
    [InlineData(Position4Mirrored, 4, 422_333)]
    [InlineData(Position5, 1, 44)]
    [InlineData(Position5, 2, 1_486)]
    [InlineData(Position5, 3, 62_379)]
    [InlineData(Position5, 4, 2_103_487)]
    [InlineData(Position6, 1, 46)]
    [InlineData(Position6, 2, 2_079)]
    [InlineData(Position6, 3, 89_890)]
    [InlineData(Position6, 4, 3_894_594)]
    public void Perft_matches_published_counts(string fen, int depth, long expected)
    {
        Assert.Equal(expected, Perft(Position.FromFen(fen), depth));
    }

    private static long Perft(Position position, int depth)
    {
        var moves = position.LegalMoves();
        if (depth == 1) return moves.Count;
        long nodes = 0;
        foreach (var move in moves) nodes += Perft(position.Play(move), depth - 1);
        return nodes;
    }
}
