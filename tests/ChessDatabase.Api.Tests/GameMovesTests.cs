using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessDatabase.Api.Tests;

/// <summary>
/// Game moves: every change to a Game's moves goes through one module, which keeps what is derived
/// from the main line (ECO code, opening name, position index) in step in the same save.
/// </summary>
public class GameMovesTests
{
    /// <summary>What is derived from a Game's main line, read back fresh from the Collection.</summary>
    private sealed record Derived(string? Eco, string? OpeningName, string[] IndexedMoves, int IndexedPositions);

    private static async Task<Derived> DerivedAsync(CollectionFactory factory, int gameId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
        var game = await db.Games.AsNoTracking().SingleAsync(g => g.Id == gameId);
        var rows = await db.PositionIndex.AsNoTracking().Where(p => p.GameId == gameId).OrderBy(p => p.Ply).ToListAsync();
        return new Derived(game.Eco, game.OpeningName, [.. rows.Where(r => r.Uci is not null).Select(r => r.Uci!)], rows.Count);
    }

    /// <summary>Opens the Game's moves in a scope of its own, lets the test change them, and saves.</summary>
    private static async Task<T> EditAsync<T>(CollectionFactory factory, int gameId, Func<GameMovesEdit, Task<T>> change)
    {
        using var scope = factory.Services.CreateScope();
        await using var edit = await scope.ServiceProvider.GetRequiredService<GameMoves>().OpenAsync(gameId)
            ?? throw new InvalidOperationException("No such Game.");
        var result = await change(edit);
        await edit.SaveAsync();
        return result;
    }

    [Fact]
    public async Task Adding_moves_classifies_the_opening_and_indexes_the_main_line()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();

        var change = await EditAsync(factory, game.Id, e => e.AddLineAsync(null, ["e4", "c5", "Nf3"]));

        Assert.Null(change.Problem);
        Assert.True(change.Last!.Id > 0);
        var derived = await DerivedAsync(factory, game.Id);
        Assert.Equal("B27", derived.Eco);
        Assert.Equal(["e2e4", "c7c5", "g1f3"], derived.IndexedMoves);
        Assert.Equal(4, derived.IndexedPositions); // the start and the position after each move
    }

    [Fact]
    public async Task Promoting_a_Variation_derives_everything_from_the_new_main_line()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        await client.PlayMainLineAsync(game.Id, "e4", "c5");
        var d4 = (await client.PlayAsync(game.Id, null, "d4")).NodeId;

        await EditAsync(factory, game.Id, e => e.PromoteAsync(d4));

        var derived = await DerivedAsync(factory, game.Id);
        Assert.Equal(["d2d4"], derived.IndexedMoves);
        Assert.StartsWith("A", derived.Eco);
    }

    [Fact]
    public async Task Deleting_from_a_main_line_move_shortens_the_index_and_reclassifies()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var main = (await client.PlayMainLineAsync(game.Id, "e4", "c5", "Nf3")).MainLine();

        await EditAsync(factory, game.Id, e => e.DeleteFromAsync(main[1].Id));

        var derived = await DerivedAsync(factory, game.Id);
        Assert.Equal(["e2e4"], derived.IndexedMoves);
        Assert.Equal("B00", derived.Eco);
    }

    [Fact]
    public async Task Changes_that_cannot_be_made_say_why_and_change_nothing()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var main = (await client.PlayMainLineAsync(game.Id, "e4", "e5")).MainLine();

        var (illegal, empty, unknown, onMainLine) = await EditAsync(factory, game.Id, async e => (
            await e.AddLineAsync(main[1].Id, ["Nf3", "Ke7", "Qh5"]),
            await e.AddLineAsync(null, []),
            await e.PromoteAsync(999_999),
            await e.DeleteVariationAsync(main[1].Id)));

        Assert.Equal((MoveChangeProblem.IllegalMove, "Qh5"), (illegal.Problem, illegal.IllegalMove));
        Assert.Equal(MoveChangeProblem.NoMoves, empty.Problem);
        Assert.Equal(MoveChangeProblem.NotInGame, unknown.Problem);
        Assert.Equal(MoveChangeProblem.OnMainLine, onMainLine.Problem);
        Assert.Equal(["e2e4", "e7e5"], (await DerivedAsync(factory, game.Id)).IndexedMoves);
        Assert.Equal(2, (await client.GetGameAsync(game.Id)).Nodes.Count);
    }

    [Fact]
    public async Task Several_changes_in_one_edit_each_see_the_moves_as_the_last_one_left_them()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var main = (await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3")).MainLine();

        var (first, again, deleted, gone, promoted) = await EditAsync(factory, game.Id, async e =>
        {
            var first = await e.AddLineAsync(main[0].Id, ["c5", "Nf3"]);  // a Variation to 1...e5
            var again = await e.AddLineAsync(main[0].Id, ["c5", "Nc3"]);  // follows 1...c5, adds 2.Nc3 beside 2.Nf3
            var deleted = await e.DeleteFromAsync(main[1].Id);              // 1...e5 goes: 1...c5 becomes the main line
            var gone = await e.PromoteAsync(main[2].Id);                    // 2.Nf3 after 1...e5 went with it
            var promoted = await e.PromoteAsync(again.Last!.Id);           // 2.Nc3 becomes the main continuation
            return (first, again, deleted, gone, promoted);
        });

        Assert.True(first.Changed && again.Changed && deleted.Changed && promoted.Changed);
        Assert.Equal(MoveChangeProblem.NotInGame, gone.Problem);
        var moves = (await client.GetGameAsync(game.Id)).Nodes;
        Assert.Equal(["e4", "c5", "Nc3", "Nf3"], moves.Select(n => n.San));        // c5 once, no duplicates
        Assert.Equal(["e2e4", "c7c5", "b1c3"], (await DerivedAsync(factory, game.Id)).IndexedMoves);
    }

    [Fact]
    public async Task Promoting_a_move_already_on_the_main_line_changes_nothing()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        var main = (await client.PlayMainLineAsync(game.Id, "e4", "e5")).MainLine();

        var change = await EditAsync(factory, game.Id, e => e.PromoteAsync(main[1].Id));

        Assert.Equal((null, false), (change.Problem, change.Changed));
    }

    [Fact]
    public async Task Changes_that_are_not_saved_are_dropped()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync();
        await client.PlayMainLineAsync(game.Id, "e4", "e5");

        using (var scope = factory.Services.CreateScope())
        {
            await using var edit = (await scope.ServiceProvider.GetRequiredService<GameMoves>().OpenAsync(game.Id))!;
            await edit.AddLineAsync(null, ["d4"]);
        }

        Assert.Equal(["e4", "e5"], (await client.GetGameAsync(game.Id)).Nodes.Select(n => n.San));
        Assert.Equal(["e2e4", "e7e5"], (await DerivedAsync(factory, game.Id)).IndexedMoves);
    }

    [Fact]
    public async Task A_Game_that_does_not_exist_cannot_be_opened()
    {
        using var factory = new CollectionFactory();
        factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        Assert.Null(await scope.ServiceProvider.GetRequiredService<GameMoves>().OpenAsync(999_999));
    }
}
