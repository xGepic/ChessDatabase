using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

/// <summary>A Player exists only while at least one Game is against them.</summary>
public class PlayerCleanupTests
{
    private static async Task<List<PlayerDto>> PlayersAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<PlayerDto>>("/api/players"))!;

    [Fact]
    public async Task Deleting_the_last_game_against_a_player_deletes_the_player()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync(opponent: "Huber, Thomas");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/games/{game.Id}")).StatusCode);

        Assert.Empty(await PlayersAsync(client));
    }

    [Fact]
    public async Task A_player_with_another_game_left_stays()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var first = await client.CreateGameAsync(opponent: "Huber, Thomas");
        await client.CreateGameAsync(opponent: "Huber, Thomas"); // the same Player again

        await client.DeleteAsync($"/api/games/{first.Id}");

        Assert.Equal(["Huber, Thomas"], (await PlayersAsync(client)).Select(p => p.Name));
    }

    [Fact]
    public async Task Choosing_another_opponent_deletes_the_previous_one_without_games()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var game = await client.CreateGameAsync(opponent: "Huber, Thomas");
        var berger = await client.CreatePlayerAsync("Berger, Anna");

        await client.UpdateDetailsAsync(game, new { opponentId = berger.Id });

        Assert.Equal(["Berger, Anna"], (await PlayersAsync(client)).Select(p => p.Name));
    }

    [Fact]
    public async Task Deleting_a_game_that_does_not_exist_is_not_found()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/games/999")).StatusCode);
    }

    [Fact]
    public async Task Players_left_without_a_game_are_removed_at_the_next_start()
    {
        var directory = CollectionFactory.NewDirectory();
        try
        {
            using (var factory = new CollectionFactory(directory))
            {
                var client = factory.CreateClient();
                await client.CreateGameAsync(opponent: "Huber, Thomas");
                await client.CreatePlayerAsync("Berger, Anna"); // created in New Game, but the Game was never started
                Assert.Equal(2, (await PlayersAsync(client)).Count);
            }

            using var restarted = new CollectionFactory(directory);
            Assert.Equal(["Huber, Thomas"], (await PlayersAsync(restarted.CreateClient())).Select(p => p.Name));
        }
        finally
        {
            CollectionFactory.DeleteDirectory(directory);
        }
    }
}
