using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class PlayerTests
{
    [Fact]
    public async Task Autocomplete_suggests_existing_players_matching_the_typed_text()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/players", new { name = "Huber, Thomas" });
        await client.PostAsJsonAsync("/api/players", new { name = "Berger, Anna" });
        await client.PostAsJsonAsync("/api/players", new { name = "Schuster, Paul" });

        var suggestions = await client.GetFromJsonAsync<List<PlayerDto>>("/api/players?search=ber");

        Assert.Equal(["Berger, Anna", "Huber, Thomas"], suggestions!.Select(p => p.Name));
    }

    [Fact]
    public async Task Creating_a_player_that_already_exists_returns_the_existing_one()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var first = await (await client.PostAsJsonAsync("/api/players", new { name = "Huber, Thomas" }))
            .Content.ReadFromJsonAsync<PlayerDto>();
        var again = await client.PostAsJsonAsync("/api/players", new { name = "huber, thomas " });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(first!.Id, (await again.Content.ReadFromJsonAsync<PlayerDto>())!.Id);
        Assert.Single((await client.GetFromJsonAsync<List<PlayerDto>>("/api/players"))!);
    }

    [Fact]
    public async Task A_player_needs_a_name()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/players", new { name = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

