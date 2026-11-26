using ChessDatabase.Rules;

namespace ChessDatabase.Rules.Tests;

public class SetupTests
{
    [Theory]
    [InlineData(Position.InitialFen)]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1")]
    [InlineData("rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 3")]
    public void Positions_that_can_occur_in_a_game_are_accepted(string fen)
    {
        Assert.Null(Position.FromFen(fen).SetupProblem());
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/P3K3 w - - 0 1", "Pawns")]
    [InlineData("4k2P/8/8/8/8/8/8/4K3 w - - 0 1", "Pawns")]
    [InlineData("4k3/8/8/8/8/8/8/4R1K1 w - - 0 1", "is in check")]
    [InlineData("4k3/8/8/8/8/8/8/R3K3 w K - 0 1", "Castling")]
    [InlineData("4k3/8/8/8/8/8/8/4K2R w Q - 0 1", "Castling")]
    [InlineData("4k3/8/8/8/8/8/8/3K3R w K - 0 1", "Castling")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - e6 0 1", "en passant")]
    public void Impossible_positions_are_refused_with_the_reason(string fen, string reason)
    {
        Assert.Contains(reason, Position.FromFen(fen).SetupProblem());
    }
}
