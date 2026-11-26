using ChessDatabase.Rules;

namespace ChessDatabase.Rules.Tests;

public class FenTests
{
    public static TheoryData<string> Fens =>
    [
        .. PerftTests.StandardPositions,
        "rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3",
        "8/8/8/8/8/4k3/8/4K3 b - - 37 112",
    ];

    [Theory]
    [MemberData(nameof(Fens))]
    public void Fen_round_trips(string fen)
    {
        Assert.Equal(fen, Position.FromFen(fen).ToFen());
    }

    [Theory]
    [InlineData("")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQ1BNR w kq - 0 1")]
    public void Invalid_fen_is_rejected(string fen)
    {
        Assert.Throws<FormatException>(() => Position.FromFen(fen));
    }

    [Fact]
    public void Playing_moves_updates_every_fen_field()
    {
        var position = Position.Initial
            .Play(Uci("e2e4")).Play(Uci("g8f6")).Play(Uci("e4e5")).Play(Uci("d7d5"));

        Assert.Equal("rnbqkb1r/ppp1pppp/5n2/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3", position.ToFen());
    }

    private static Move Uci(string uci) => Move.TryParseUci(uci, out var m) ? m : throw new ArgumentException(uci);
}
