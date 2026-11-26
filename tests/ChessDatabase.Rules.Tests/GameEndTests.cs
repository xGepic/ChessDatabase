using ChessDatabase.Rules;

namespace ChessDatabase.Rules.Tests;

public class GameEndTests
{
    [Fact]
    public void Fools_mate_is_checkmate()
    {
        var position = Position.FromFen("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3");

        Assert.True(position.IsCheck);
        Assert.True(position.IsCheckmate);
        Assert.False(position.IsStalemate);
        Assert.Empty(position.LegalMoves());
    }

    [Fact]
    public void King_with_no_moves_and_not_in_check_is_stalemate()
    {
        var position = Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");

        Assert.False(position.IsCheck);
        Assert.False(position.IsCheckmate);
        Assert.True(position.IsStalemate);
    }

    [Fact]
    public void Check_that_can_be_answered_is_not_mate()
    {
        var position = Position.FromFen("R3k3/8/8/8/8/8/8/4K3 b - - 0 1");

        Assert.True(position.IsCheck);
        Assert.False(position.IsCheckmate);
        Assert.NotEmpty(position.LegalMoves());
    }

    [Fact]
    public void Playing_an_illegal_move_throws()
    {
        Move.TryParseUci("e2e5", out var move);

        Assert.Throws<IllegalMoveException>(() => Position.Initial.Play(move));
    }
}
