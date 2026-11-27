using ChessDatabase.Rules;

namespace ChessDatabase.Rules.Tests;

/// <summary>A line: moves played one after another from a position, replayed by Chess Rules once.</summary>
public class LineTests
{
    [Fact]
    public void Stored_moves_give_every_position_and_the_moves_in_uci_and_san()
    {
        var line = Line.FromUci(["e2e4", "c7c5", "g1f3"]);

        Assert.Equal(3, line.Count);
        Assert.Equal(["e4", "c5", "Nf3"], line.San);
        Assert.Equal(["e2e4", "c7c5", "g1f3"], line.Uci);
        Assert.Equal(4, line.Positions.Count); // the start, then the position after each move
        Assert.Equal(Position.InitialFen, line.Positions[0].ToFen());
        Assert.Equal("rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2", line.Positions[3].ToFen());
        Assert.Equal(line.Positions[3], line.End);
    }

    [Fact]
    public void No_moves_is_the_starting_position_alone()
    {
        var line = Line.FromUci([]);

        Assert.Equal(0, line.Count);
        Assert.Equal(Position.InitialFen, Assert.Single(line.Positions).ToFen());
    }

    [Fact]
    public void A_stored_move_that_is_not_uci_or_not_legal_is_a_fault()
    {
        Assert.Throws<FormatException>(() => Line.FromUci(["e4"]));
        Assert.Throws<IllegalMoveException>(() => Line.FromUci(["e2e4", "e2e4"]));
    }

    [Fact]
    public void Typed_moves_are_read_as_uci_or_san_with_german_letters_from_any_position()
    {
        var start = Line.FromUci(["e2e4", "e7e5"]).End;

        var line = Line.Read(start, ["Sf3", "b8c6", "Lb5"], out var illegal);

        Assert.Null(illegal);
        Assert.Equal(["Nf3", "Nc6", "Bb5"], line!.San);
        Assert.Equal(start, line.Positions[0]);
    }

    [Fact]
    public void Reading_stops_at_the_first_move_that_is_not_legal()
    {
        var line = Line.Read(Position.Initial, ["e4", "e5", "Ke3", "Nf3"], out var illegal);

        Assert.Null(line);
        Assert.Equal("Ke3", illegal);
    }

    [Theory]
    [InlineData("e2e4", true)]
    [InlineData(" e4 ", true)]
    [InlineData("Sf3", true)]
    [InlineData("e2e5", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_single_typed_move_is_read_if_it_is_legal(string? text, bool legal)
    {
        Assert.Equal(legal, Position.Initial.TryReadMove(text, out _));
    }

    [Fact]
    public void Parsing_a_stored_uci_move_that_is_not_uci_is_a_fault()
    {
        Assert.Equal(new Move(Square.Parse("e7"), Square.Parse("e8"), PieceType.Queen), Move.ParseUci("e7e8q"));
        Assert.Throws<FormatException>(() => Move.ParseUci("Nf3"));
    }
}
