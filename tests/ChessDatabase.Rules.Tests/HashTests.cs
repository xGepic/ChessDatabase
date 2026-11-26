using ChessDatabase.Rules;

namespace ChessDatabase.Rules.Tests;

public class HashTests
{
    [Fact]
    public void Positions_reached_by_different_move_orders_have_equal_hashes()
    {
        var a = PlaySan(Position.Initial, "Nf3", "Nf6", "Nc3", "Nc6");
        var b = PlaySan(Position.Initial, "Nc3", "Nc6", "Nf3", "Nf6");

        Assert.Equal(a.Hash, b.Hash);
    }

    [Fact]
    public void Transposition_into_a_queens_gambit_hashes_equal()
    {
        var a = PlaySan(Position.Initial, "d4", "d5", "c4", "e6", "Nc3", "Nf6");
        var b = PlaySan(Position.Initial, "c4", "e6", "Nc3", "d5", "d4", "Nf6");

        Assert.Equal(a.Hash, b.Hash);
    }

    [Fact]
    public void Side_to_move_changes_the_hash()
    {
        var white = Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1");
        var black = Position.FromFen("4k3/8/8/8/8/8/8/4K3 b - - 0 1");

        Assert.NotEqual(white.Hash, black.Hash);
    }

    [Theory]
    [InlineData("KQk")]
    [InlineData("KQ")]
    [InlineData("Qkq")]
    [InlineData("-")]
    public void Castling_rights_change_the_hash(string castling)
    {
        var all = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        var fewer = Position.FromFen($"r3k2r/8/8/8/8/8/8/R3K2R w {castling} - 0 1");

        Assert.NotEqual(all.Hash, fewer.Hash);
    }

    [Fact]
    public void Capturable_en_passant_square_changes_the_hash()
    {
        var withEp = Position.FromFen("rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3");
        var withoutEp = Position.FromFen("rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq - 0 3");

        Assert.NotEqual(withEp.Hash, withoutEp.Hash);
    }

    [Fact]
    public void En_passant_square_nobody_can_capture_on_does_not_change_the_hash()
    {
        var withEp = Position.FromFen("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1");
        var withoutEp = Position.FromFen("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1");

        Assert.Equal(withEp.Hash, withoutEp.Hash);
    }

    [Fact]
    public void Move_counters_do_not_change_the_hash()
    {
        Assert.Equal(
            Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").Hash,
            Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 12 40").Hash);
    }

    private static Position PlaySan(Position position, params string[] moves) =>
        moves.Aggregate(position, (p, san) => p.Play(p.ParseSan(san)));
}
