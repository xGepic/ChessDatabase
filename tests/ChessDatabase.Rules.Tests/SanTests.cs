using ChessDatabase.Rules;

namespace ChessDatabase.Rules.Tests;

public class SanTests
{
    public static TheoryData<string> PerftPositions => [.. PerftTests.StandardPositions];

    [Theory]
    [MemberData(nameof(PerftPositions))]
    public void Every_legal_move_two_plies_deep_formats_to_san_and_parses_back(string fen)
    {
        var root = Position.FromFen(fen);
        foreach (var position in new[] { root }.Concat(root.LegalMoves().Select(root.Play)))
        {
            foreach (var move in position.LegalMoves())
            {
                var san = position.ToSan(move);
                Assert.Equal(move, position.ParseSan(san));
            }
        }
    }

    [Theory]
    // disambiguation by file, by rank, and by both
    [InlineData("1k6/8/8/8/8/8/4K3/R6R w - - 0 1", "a1d1", "Rad1")]
    [InlineData("7k/8/8/R7/8/8/8/R3K3 w - - 0 1", "a1a3", "R1a3")]
    [InlineData("7k/8/8/8/Q1Q5/8/Q7/4K3 w - - 0 1", "a4b3", "Qa4b3")]
    // captures, en passant, promotion, castling
    [InlineData("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2", "e4d5", "exd5")]
    [InlineData("rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3", "e5d6", "exd6")]
    [InlineData("8/P6k/8/8/8/8/8/4K3 w - - 0 1", "a7a8q", "a8=Q")]
    [InlineData("8/P6k/8/8/8/8/8/4K3 w - - 0 1", "a7a8n", "a8=N")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1", "O-O")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1", "e8c8", "O-O-O")]
    // check and mate suffixes
    [InlineData("4k3/8/8/8/8/8/8/R3K3 w - - 0 1", "a1a8", "Ra8+")]
    [InlineData("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1", "a1a8", "Ra8#")]
    [InlineData("rnbqkbnr/pppp1ppp/8/4p3/6P1/5P2/PPPPP2P/RNBQKBNR b KQkq g3 0 2", "d8h4", "Qh4#")]
    public void Formats_known_san(string fen, string uci, string expectedSan)
    {
        var position = Position.FromFen(fen);
        Move.TryParseUci(uci, out var move);

        Assert.Equal(expectedSan, position.ToSan(move));
        Assert.Equal(move, position.ParseSan(expectedSan));
    }

    [Theory]
    [InlineData("Sf3", "Nf3")]
    [InlineData("Lb5", "Bb5")]
    [InlineData("Dxd8+", "Qxd8+")]
    [InlineData("Txe1", "Rxe1")]
    [InlineData("Kh3", "Kh3")]
    public void German_piece_letters_parse_to_the_same_move_as_english(string german, string english)
    {
        // Every move above is legal for White here: Ng1, Bf1, Qd2 vs Qd8, Ra1 vs Ne1, Kh2.
        var position = Position.FromFen("3qk3/8/8/8/8/8/3Q3K/R3nBN1 w - - 0 1");

        Assert.Equal(position.ParseSan(english), position.ParseSan(german));
    }

    [Fact]
    public void German_promotion_letter_is_accepted()
    {
        var position = Position.FromFen("8/P6k/8/8/8/8/8/4K3 w - - 0 1");

        Assert.Equal(position.ParseSan("a8=Q"), position.ParseSan("a8=D"));
    }

    [Theory]
    [InlineData("e4", "e2e4")]
    [InlineData("e4!?", "e2e4")]
    [InlineData("Nc3", "b1c3")]
    [InlineData("0-0", "e1g1")]
    public void Accepts_common_variants(string san, string uci)
    {
        var position = Position.FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQK2R w KQkq - 0 1");
        Move.TryParseUci(uci, out var move);

        Assert.Equal(move, position.ParseSan(san));
    }

    [Theory]
    [InlineData("e5")]      // illegal
    [InlineData("Nd2")]     // occupied by own piece
    [InlineData("Ra2")]     // no rook can reach
    [InlineData("Xe4")]     // not a piece
    [InlineData("")]
    public void Rejects_invalid_san(string san)
    {
        Assert.False(Position.Initial.TryParseSan(san, out _));
        Assert.Throws<IllegalMoveException>(() => Position.Initial.ParseSan(san));
    }

    [Fact]
    public void Rejects_ambiguous_san()
    {
        var position = Position.FromFen("1k6/8/8/8/8/8/4K3/R6R w - - 0 1");

        Assert.False(position.TryParseSan("Rd1", out _));
    }
}
