using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class DossierTests
{
    /// <summary>
    /// Four Games against Huber and one against someone else:
    /// 2024-01-10 Owner White 1-0, Huber unrated, 1.e4 e5 (C20)
    /// 2024-03-05 Owner Black ½-½, Huber 1850, 1.e4 c5 (B20)
    /// 2024-06-20 Owner White 0-1, Huber 1900, 1.e4 e5 2.Nf3 (C40)
    /// no date    Owner Black 0-1 (a win), Huber 1880, 1.d4 d5 (D00)
    /// </summary>
    private static async Task<(HttpClient Client, int Huber, int[] GameIds)> SeedAsync(CollectionFactory factory)
    {
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var huber = await client.CreatePlayerAsync("Huber, Thomas");
        var other = await client.CreatePlayerAsync("Someone, Else");
        var ids = new List<int>
        {
            await GameAsync(client, huber.Id, "white", "1-0", "2024-01-10", whiteElo: 2000, blackElo: null, "e4", "e5"),
            await GameAsync(client, huber.Id, "black", "1/2-1/2", "2024-03-05", whiteElo: 1850, blackElo: 2010, "e4", "c5"),
            await GameAsync(client, huber.Id, "white", "0-1", "2024-06-20", whiteElo: 2020, blackElo: 1900, "e4", "e5", "Nf3"),
            await GameAsync(client, huber.Id, "black", "0-1", null, whiteElo: 1880, blackElo: 2030, "d4", "d5"),
        };
        await GameAsync(client, other.Id, "white", "1-0", "2024-02-01", whiteElo: 2000, blackElo: 2100, "c4");
        return (client, huber.Id, [.. ids]);
    }

    private static async Task<int> GameAsync(
        HttpClient client, int opponentId, string ownerColour, string result, string? date, int? whiteElo, int? blackElo, params string[] moves)
    {
        var response = await client.PostAsJsonAsync("/api/games", new { ownerColour, opponentId, result, date, whiteElo, blackElo });
        response.EnsureSuccessStatusCode();
        var game = (await response.Content.ReadFromJsonAsync<GameDto>())!;
        await client.PlayMainLineAsync(game.Id, moves);
        return game.Id;
    }

    [Fact]
    public async Task The_Dossier_shows_the_head_to_head_score_from_the_Owners_side()
    {
        using var factory = new CollectionFactory();
        var (client, huber, _) = await SeedAsync(factory);

        var dossier = await client.DossierAsync(huber);

        Assert.Equal("Huber, Thomas", dossier.Player.Name);
        Assert.Equal(new ScoreDto(4, 2, 1, 1, 62.5), dossier.Score);
    }

    [Fact]
    public async Task The_rating_history_uses_the_Players_recorded_Elo_and_leaves_unrated_Games_out()
    {
        using var factory = new CollectionFactory();
        var (client, huber, ids) = await SeedAsync(factory);

        var dossier = await client.DossierAsync(huber);

        // 2024-01 was unrated; the Game without a date can't be placed on the time line.
        Assert.Equal([("2024-03-05", 1850, ids[1]), ("2024-06-20", 1900, ids[2])], dossier.RatingHistory.Select(p => (p.Date, p.Elo, p.GameId)));
    }

    [Fact]
    public async Task The_openings_against_the_Owner_are_grouped_by_ECO_with_results()
    {
        using var factory = new CollectionFactory();
        var (client, huber, _) = await SeedAsync(factory);

        var dossier = await client.DossierAsync(huber);

        var byEco = dossier.Openings.ToDictionary(o => o.Eco ?? "-");
        Assert.Equal(4, byEco.Count);
        Assert.Equal(new ScoreDto(1, 1, 0, 0, 100), byEco["C20"].Score);
        Assert.Equal(new ScoreDto(1, 0, 1, 0, 50), byEco["B20"].Score);
        Assert.Equal(new ScoreDto(1, 0, 0, 1, 0), byEco["C40"].Score);
        Assert.Equal("Sicilian Defense", byEco["B20"].Name);
    }

    [Fact]
    public async Task The_Games_against_the_Player_are_listed_newest_first()
    {
        using var factory = new CollectionFactory();
        var (client, huber, ids) = await SeedAsync(factory);

        var dossier = await client.DossierAsync(huber);

        Assert.Equal([ids[2], ids[1], ids[0], ids[3]], dossier.Games.Select(g => g.Id));
    }

    [Fact]
    public async Task The_Players_list_searches_and_shows_each_score()
    {
        using var factory = new CollectionFactory();
        var (client, huber, _) = await SeedAsync(factory);

        var all = (await client.GetFromJsonAsync<List<PlayerRowDto>>("/api/players/overview"))!;
        var found = (await client.GetFromJsonAsync<List<PlayerRowDto>>("/api/players/overview?search=hub"))!;

        Assert.Equal(["Huber, Thomas", "Someone, Else"], all.Select(p => p.Name));
        var row = Assert.Single(found);
        Assert.Equal((huber, 4, 62.5, "2024-06-20"), (row.Id, row.Score.Games, row.Score.Percent, row.LastDate));
    }

    [Fact]
    public async Task FIDE_ID_and_club_can_be_recorded()
    {
        using var factory = new CollectionFactory();
        var (client, huber, _) = await SeedAsync(factory);

        var put = await client.PutAsJsonAsync($"/api/players/{huber}", new { name = "Huber, Thomas", fideId = " 1601234 ", club = "SK Neustadt" });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var player = (await client.DossierAsync(huber)).Player;
        Assert.Equal(("1601234", "SK Neustadt"), (player.FideId, player.Club));
    }

    [Theory]
    [InlineData("", "1601234", "name")]
    [InlineData("Huber, Thomas", "16-01", "fideId")]
    [InlineData("Someone, Else", "", "name")]
    public async Task Invalid_details_are_refused(string name, string fideId, string field)
    {
        using var factory = new CollectionFactory();
        var (client, huber, _) = await SeedAsync(factory);

        var put = await client.PutAsJsonAsync($"/api/players/{huber}", new { name, fideId, club = "" });

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains($"\"{field}\"", await put.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_Player_has_no_Dossier()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/players/999/dossier")).StatusCode);
    }
}

public sealed record ScoreDto(int Games, int Wins, int Draws, int Losses, double Percent);

public sealed record PlayerDetailsDto(int Id, string Name, string? FideId, string? Club);

public sealed record RatingPointDto(string Date, int Elo, int GameId);

public sealed record OpeningScoreDto(string? Eco, string? Name, ScoreDto Score);

public sealed record DossierDto(
    PlayerDetailsDto Player, ScoreDto Score, List<RatingPointDto> RatingHistory, List<OpeningScoreDto> Openings, List<GameSummaryDto> Games);

public sealed record PlayerRowDto(int Id, string Name, string? FideId, string? Club, ScoreDto Score, string? LastDate);

public static class DossierTestExtensions
{
    public static async Task<DossierDto> DossierAsync(this HttpClient client, int playerId) =>
        (await client.GetFromJsonAsync<DossierDto>($"/api/players/{playerId}/dossier"))!;
}
