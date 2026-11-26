using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class GameListTests
{
    [Fact]
    public async Task Fresh_collection_has_an_empty_game_list()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var games = await client.GetFromJsonAsync<List<object>>("/api/games");

        Assert.NotNull(games);
        Assert.Empty(games);
    }
}
