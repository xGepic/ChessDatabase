using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace ChessDatabase.Api.Tests;

/// <summary>Reading Games' main lines: one module, whichever stored copy a question needs.</summary>
public class MainLinesTests
{
    [Theory]
    [InlineData("e2e4 e7e5 g1f3", "e2e4 e7e5 g1f3", 3)]  // unchanged
    [InlineData("e2e4 e7e5 g1f3", "e2e4 c7c5 g1f3", 1)]  // changed at the second move
    [InlineData("e2e4 e7e5", "e2e4 e7e5 g1f3", 2)]       // moves taken back since
    [InlineData("e2e4 e7e5 g1f3 b8c6", "e2e4 e7e5", 2)]  // moves added since
    [InlineData("", "e2e4", 0)]
    public void How_far_a_Game_still_follows_the_analysed_moves(string current, string analysed, int plies)
    {
        Assert.Equal(plies, MainLines.FollowedPlies(Split(current), Split(analysed)));
    }

    [Fact]
    public async Task Both_copies_give_the_same_main_line_for_many_Games_at_once()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var first = await client.CreateGameAsync();
        var main = (await client.PlayMainLineAsync(first.Id, "e4", "e5", "Nf3")).MainLine();
        await client.PlayAsync(first.Id, main[0].Id, "c5");   // a Variation, not the main line
        var second = await client.CreateGameAsync();
        await client.PlayMainLineAsync(second.Id, "d4");
        int[] ids = [first.Id, second.Id, 999_999];

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
        var indexed = await MainLines.IndexedAsync(db, ids);
        var moves = await MainLines.MovesAsync(db, ids);

        Assert.Equal(["e2e4", "e7e5", "g1f3"], indexed[first.Id].Uci);
        Assert.Equal(["e4", "e5", "Nf3"], moves[first.Id].Select(n => n.San));
        Assert.Equal(main.Select(n => n.Id), moves[first.Id].Select(n => n.Id));
        Assert.Equal(Line.FromUci(["e2e4", "e7e5", "g1f3"]).Positions.Select(p => PositionIndex.Key(p)), indexed[first.Id].PositionKeys);
        Assert.Equal(["d2d4"], indexed[second.Id].Uci);
        Assert.Empty(indexed[999_999].Uci);
        Assert.Empty(moves[999_999]);
    }

    private static string[] Split(string moves) => moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
