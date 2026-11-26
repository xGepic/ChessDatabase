using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class MergePlayersTests
{
    private static async Task<(HttpClient Client, int Full, int Short, int Other)> SeedAsync(CollectionFactory factory)
    {
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var full = await client.CreatePlayerAsync("Huber, Thomas");
        var shortName = await client.CreatePlayerAsync("Huber T.");
        var other = await client.CreatePlayerAsync("Someone, Else");
        await client.PutAsJsonAsync($"/api/players/{shortName.Id}", new { name = "Huber T.", fideId = "1601234", club = "" });
        await client.PutAsJsonAsync($"/api/players/{full.Id}", new { name = "Huber, Thomas", fideId = "", club = "SK Neustadt" });
        await GameAsync(client, full.Id, "1-0", "2024-01-10", 1850);
        await GameAsync(client, shortName.Id, "0-1", "2024-03-05", 1900);
        await GameAsync(client, shortName.Id, "1/2-1/2", "2024-06-20", null);
        await GameAsync(client, other.Id, "1-0", "2024-02-01", null);
        return (client, full.Id, shortName.Id, other.Id);
    }

    private static async Task GameAsync(HttpClient client, int opponentId, string result, string date, int? blackElo) =>
        (await client.PostAsJsonAsync("/api/games", new { ownerColour = "white", opponentId, result, date, blackElo })).EnsureSuccessStatusCode();

    [Fact]
    public async Task Merging_moves_every_Game_to_the_kept_Player_with_the_chosen_details()
    {
        using var factory = new CollectionFactory();
        var (client, full, shortName, _) = await SeedAsync(factory);

        var response = await client.PostAsJsonAsync($"/api/players/{full}/merge",
            new { mergeId = shortName, name = "Huber, Thomas", fideId = "1601234", club = "SK Neustadt" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dossier = await client.DossierAsync(full);
        Assert.Equal(new PlayerDetailsDto(full, "Huber, Thomas", "1601234", "SK Neustadt"), dossier.Player);
        Assert.Equal(new ScoreDto(3, 1, 1, 1, 50), dossier.Score);
        Assert.Equal([("2024-01-10", 1850), ("2024-03-05", 1900)], dossier.RatingHistory.Select(p => (p.Date, p.Elo)));
        Assert.Equal(3, dossier.Games.Count);
        Assert.All(await client.ListGamesAsync(), g => Assert.NotEqual(shortName, g.Opponent.Id));
    }

    [Fact]
    public async Task The_merged_Player_disappears()
    {
        using var factory = new CollectionFactory();
        var (client, full, shortName, _) = await SeedAsync(factory);

        await client.PostAsJsonAsync($"/api/players/{full}/merge", new { mergeId = shortName, name = "Huber, Thomas" });

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/players/{shortName}/dossier")).StatusCode);
        var players = (await client.GetFromJsonAsync<List<PlayerRowDto>>("/api/players/overview"))!;
        Assert.Equal(["Huber, Thomas", "Someone, Else"], players.Select(p => p.Name));
    }

    [Fact]
    public async Task The_merged_Players_name_can_be_the_one_kept()
    {
        using var factory = new CollectionFactory();
        var (client, full, shortName, _) = await SeedAsync(factory);

        var response = await client.PostAsJsonAsync($"/api/players/{full}/merge", new { mergeId = shortName, name = "Huber T." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Huber T.", (await client.DossierAsync(full)).Player.Name);
        Assert.Equal(["Huber T.", "Someone, Else"], (await client.GetFromJsonAsync<List<PlayerRowDto>>("/api/players/overview"))!.Select(p => p.Name));
    }

    [Fact]
    public async Task A_name_taken_by_a_third_Player_is_refused()
    {
        using var factory = new CollectionFactory();
        var (client, full, shortName, _) = await SeedAsync(factory);

        var response = await client.PostAsJsonAsync($"/api/players/{full}/merge", new { mergeId = shortName, name = "Someone, Else" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2, (await client.DossierAsync(shortName)).Games.Count); // nothing changed
    }

    [Fact]
    public async Task A_Player_cannot_be_merged_with_itself_or_with_nobody()
    {
        using var factory = new CollectionFactory();
        var (client, full, _, _) = await SeedAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/players/{full}/merge", new { mergeId = full, name = "Huber, Thomas" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/players/{full}/merge", new { mergeId = 999, name = "Huber, Thomas" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/players/999/merge", new { mergeId = full, name = "Huber, Thomas" })).StatusCode);
    }
}
