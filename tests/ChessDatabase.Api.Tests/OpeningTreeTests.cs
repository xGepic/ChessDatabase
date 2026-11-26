using System.Net;
using System.Net.Http.Json;
using ChessDatabase.Api.Collection;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChessDatabase.Api.Tests;

public class OpeningTreeTests
{
    [Fact]
    public async Task The_tree_shows_every_move_played_from_a_position_with_its_Game_count()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        await client.SeedGameAsync("1-0", "e4", "e5");
        await client.SeedGameAsync("0-1", "e4", "c5");
        await client.SeedGameAsync("1/2-1/2", "d4", "d5");

        var tree = await client.TreeAsync();

        Assert.Equal(3, tree.Games);
        Assert.Equal([("e4", "e2e4", 2), ("d4", "d2d4", 1)], tree.Moves.Select(m => (m.San, m.Uci, m.Games)));
    }

    [Fact]
    public async Task Scores_are_the_Owners_whichever_colour_the_Owner_had()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        await client.SeedGameAsync("1-0", "e4", "c5");          // won with White
        await client.SeedBlackGameAsync("1-0", "e4", "c5");     // lost with Black
        await client.SeedBlackGameAsync("1/2-1/2", "e4", "c5"); // drew with Black
        await client.SeedBlackGameAsync("0-1", "e4", "e5");     // won with Black

        var tree = await client.TreeAsync("e4");

        var c5 = tree.Moves.Single(m => m.San == "c5");
        Assert.Equal((3, 1, 1, 1, 50.0), (c5.Games, c5.Wins, c5.Draws, c5.Losses, c5.Score));
        var e5 = tree.Moves.Single(m => m.San == "e5");
        Assert.Equal((1, 1, 0, 0, 100.0), (e5.Games, e5.Wins, e5.Draws, e5.Losses, e5.Score));
    }

    [Fact]
    public async Task Positions_reached_by_different_move_orders_are_counted_together()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        await client.SeedGameAsync("1-0", "d4", "Nf6", "c4", "e6", "Nc3", "Bb4");
        await client.SeedGameAsync("0-1", "c4", "e6", "Nc3", "Nf6", "d4", "Bb4");
        await client.SeedGameAsync("1/2-1/2", "Nf3", "Nf6", "c4", "e6", "Nc3", "d5");

        var tree = await client.TreeAsync("d4", "Nf6", "c4", "e6", "Nc3");

        Assert.Equal(2, tree.Games);
        var bb4 = Assert.Single(tree.Moves);
        Assert.Equal(("Bb4", 2, 50.0), (bb4.San, bb4.Games, bb4.Score));
    }

    [Fact]
    public async Task Variation_moves_never_appear_and_promoting_one_updates_the_tree_at_once()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.SeedGameAsync("1-0", "e4", "e5", "Nf3");
        var bc4 = (await client.PlayAsync(game.Id, game.MainLine()[1].Id, "Bc4")).NodeId;

        var before = await client.TreeAsync("e4", "e5");
        Assert.Equal(["Nf3"], before.Moves.Select(m => m.San));
        Assert.Empty((await client.TreeAsync("e4", "e5", "Bc4")).GameList);

        await client.PostAsync($"/api/games/{game.Id}/moves/{bc4}/promote", null);

        Assert.Equal(["Bc4"], (await client.TreeAsync("e4", "e5")).Moves.Select(m => m.San));
        Assert.Single((await client.TreeAsync("e4", "e5", "Bc4")).GameList);
        Assert.Empty((await client.TreeAsync("e4", "e5", "Nf3")).GameList);
    }

    [Fact]
    public async Task Deleting_main_line_moves_updates_the_tree()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.SeedGameAsync("1-0", "e4", "e5", "Nf3");

        await client.DeleteAsync($"/api/games/{game.Id}/moves/{game.MainLine()[1].Id}");

        var tree = await client.TreeAsync("e4");
        Assert.Empty(tree.Moves);
        Assert.Equal(1, tree.Games); // the Game still ends in this position
    }

    [Fact]
    public async Task The_Games_that_reached_the_position_are_listed_with_where_it_occurred()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var older = await client.SeedGameAsync("1-0", "Nf3", "d5", "d4", "Nf6");
        await client.UpdateDetailsAsync(older, new { date = "2023-05-01" });
        var newer = await client.SeedGameAsync("0-1", "d4", "d5", "Nf3", "Nf6", "c4");
        await client.UpdateDetailsAsync(newer, new { date = "2024-11-02" });

        var tree = await client.TreeAsync("d4", "d5", "Nf3", "Nf6");

        Assert.Equal([(newer.Id, 4, 3), (older.Id, 4, 3)], tree.GameList.Select(g => (g.Game.Id, g.Ply, g.MoveNumber)));
        Assert.Equal(["c4"], tree.Moves.Select(m => m.San));
        Assert.Equal(2, tree.Games);
    }

    [Fact]
    public async Task Imported_Games_are_in_the_tree()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");

        await client.ImportTextAsync("""
            [White "Mustermann, Max"]
            [Black "Huber, Thomas"]
            [Result "1-0"]

            1. e4 c5 (1... e5 2. Nf3) 2. Nf3 1-0
            """);

        var tree = await client.TreeAsync("e4");
        Assert.Equal(["c5"], tree.Moves.Select(m => m.San));
    }

    [Fact]
    public async Task Games_that_existed_before_the_index_are_indexed_on_start()
    {
        var directory = CollectionFactory.NewDirectory();
        try
        {
            using (var first = new CollectionFactory(directory))
            {
                var client = first.CreateClient();
                await client.SetOwnerNameAsync("Max Mustermann");
                await client.SeedGameAsync("1-0", "e4", "e5");
                // As if the Game had been stored before the index existed.
                using var scope = first.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CollectionDbContext>().PositionIndex.ExecuteDeleteAsync();
                Assert.Empty((await client.TreeAsync()).Moves);
            }

            using var second = new CollectionFactory(directory);
            var tree = await second.CreateClient().TreeAsync();
            Assert.Equal(["e4"], tree.Moves.Select(m => m.San));
        }
        finally
        {
            CollectionFactory.DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task A_position_that_is_not_a_valid_FEN_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/research/tree?fen=not-a-fen")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/research/tree")).StatusCode);
    }
}

public sealed record TreeMoveDto(string Uci, string San, int Games, int Wins, int Draws, int Losses, double Score);

public sealed record PositionGameDto(GameSummaryDto Game, int Ply, int MoveNumber);

public sealed record OpeningTreeDto(int Games, List<TreeMoveDto> Moves, List<PositionGameDto> GameList);

public static class ResearchTestExtensions
{
    /// <summary>A Game the Owner played as White with this main line (SAN).</summary>
    public static Task<GameDto> SeedGameAsync(this HttpClient client, string result, params string[] moves) =>
        client.SeedAsync("white", result, moves);

    /// <summary>A Game the Owner played as Black with this main line (SAN).</summary>
    public static Task<GameDto> SeedBlackGameAsync(this HttpClient client, string result, params string[] moves) =>
        client.SeedAsync("black", result, moves);

    private static async Task<GameDto> SeedAsync(this HttpClient client, string ownerColour, string result, string[] moves)
    {
        var game = await client.CreateGameAsync(ownerColour: ownerColour, opponent: $"Opponent {Guid.NewGuid():N}", result: result);
        return await client.PlayMainLineAsync(game.Id, moves);
    }

    /// <summary>The Opening Tree for the position after these SAN moves from the start.</summary>
    public static async Task<OpeningTreeDto> TreeAsync(this HttpClient client, params string[] moves) =>
        (await client.GetFromJsonAsync<OpeningTreeDto>($"/api/research/tree?fen={Uri.EscapeDataString(Fen(moves))}"))!;

    public static string Fen(params string[] moves)
    {
        var position = Position.Initial;
        foreach (var san in moves) position = position.Play(position.ParseSan(san));
        return position.ToFen();
    }
}
