using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class OwnerStatisticsTests
{
    /// <summary>
    /// G1 2024-01-10 White 1-0  me 2000 vs 1900, 90+30, 1.e4 e5 (C20)
    /// G2 2024-02-10 Black ½-½  me 2010 vs 1950, 90+30, 1.e4 c5 (B20)
    /// G3 2024-03-10 White 0-1  me 2020 vs unrated, 15+10, 1.e4 e5 (C20)
    /// G4 2024-04-10 Black 0-1  me 2030 vs 1800, 90+30, 1.d4 d5 (D00)
    /// G5 2024-05-10 White 1-0  me unrated vs 1700, 90+30, Scholar's Mate, analysed (my Accuracy 88.0)
    /// </summary>
    private static async Task<(HttpClient Client, int[] Ids, FakeEngine Engine)> SeedAsync(CollectionFactory factory)
    {
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var engine = GameAnalysisTests.ScholarsMateEngine();
        await client.UseAsync(engine);
        var ids = new[]
        {
            await GameAsync(client, "white", "1-0", "2024-01-10", 2000, 1900, "90+30", "e4", "e5"),
            await GameAsync(client, "black", "1/2-1/2", "2024-02-10", 1950, 2010, "90+30", "e4", "c5"),
            await GameAsync(client, "white", "0-1", "2024-03-10", 2020, null, "15+10", "e4", "e5"),
            await GameAsync(client, "black", "0-1", "2024-04-10", 1800, 2030, "90+30", "d4", "d5"),
            await GameAsync(client, "white", "1-0", "2024-05-10", null, 1700, "90+30", "e4", "e5", "Qh5", "Nc6", "Bc4", "Nf6", "Qxf7#"),
        };
        await client.PostAsJsonAsync($"/api/games/{ids[4]}/analysis", new { });
        await client.WaitForAssessmentAsync(ids[4]);
        return (client, ids, engine);
    }

    private static async Task<int> GameAsync(
        HttpClient client, string ownerColour, string result, string date, int? whiteElo, int? blackElo, string timeControl, params string[] moves)
    {
        var opponent = await client.CreatePlayerAsync($"Opponent {Guid.NewGuid():N}");
        var response = await client.PostAsJsonAsync("/api/games", new { ownerColour, opponentId = opponent.Id, result, date, whiteElo, blackElo });
        response.EnsureSuccessStatusCode();
        var game = (await response.Content.ReadFromJsonAsync<GameDto>())!;
        await client.UpdateDetailsAsync(game, new { timeControl });
        await client.PlayMainLineAsync(game.Id, moves);
        return game.Id;
    }

    [Fact]
    public async Task Scores_overall_by_colour_and_by_opening_and_colour()
    {
        using var factory = new CollectionFactory();
        var (client, _, engine) = await SeedAsync(factory);
        using var _e = engine;

        var stats = await client.OwnerStatisticsAsync();

        Assert.Equal(new ScoreDto(5, 3, 1, 1, 70), stats.Score);
        Assert.Equal(new ScoreDto(3, 2, 0, 1, 66.7), stats.White);
        Assert.Equal(new ScoreDto(2, 1, 1, 0, 75), stats.Black);
        Assert.Equal(
            [("C20", "white", new ScoreDto(3, 2, 0, 1, 66.7)), ("B20", "black", new ScoreDto(1, 0, 1, 0, 50)), ("D00", "black", new ScoreDto(1, 1, 0, 0, 100))],
            stats.Openings.Select(o => (o.Eco, o.OwnerColour, o.Score)));
    }

    [Fact]
    public async Task Rating_history_and_average_opponent_rating_leave_unrated_values_out()
    {
        using var factory = new CollectionFactory();
        var (client, ids, engine) = await SeedAsync(factory);
        using var _e = engine;

        var stats = await client.OwnerStatisticsAsync();

        Assert.Equal([2000, 2010, 2020, 2030], stats.RatingHistory.Select(p => p.Elo));
        Assert.Equal(ids[..4], stats.RatingHistory.Select(p => p.GameId));
        Assert.Equal(1837.5, stats.AverageOpponentElo);
        Assert.Equal(4, stats.RatedOpponentGames);
    }

    [Fact]
    public async Task Accuracy_over_time_includes_only_analysed_Games()
    {
        using var factory = new CollectionFactory();
        var (client, ids, engine) = await SeedAsync(factory);
        using var _e = engine;

        var stats = await client.OwnerStatisticsAsync();

        Assert.Equal([("2024-05-10", 88.0, ids[4])], stats.Accuracy.Select(p => (p.Date, p.Accuracy, p.GameId)));
    }

    [Fact]
    public async Task The_date_range_applies_to_every_figure()
    {
        using var factory = new CollectionFactory();
        var (client, ids, engine) = await SeedAsync(factory);
        using var _e = engine;

        var stats = await client.OwnerStatisticsAsync("from=2024-02&to=2024-04");

        Assert.Equal(new ScoreDto(3, 1, 1, 1, 50), stats.Score);
        Assert.Equal(ids[1..4], stats.RatingHistory.Select(p => p.GameId));
        Assert.Equal(1875, stats.AverageOpponentElo);
        Assert.Empty(stats.Accuracy);
    }

    [Fact]
    public async Task An_invalid_filter_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/statistics/owner?from=March")).StatusCode);
    }
}

public sealed record OwnerOpeningScoreDto(string? Eco, string? Name, string OwnerColour, ScoreDto Score);

public sealed record AccuracyPointDto(string Date, double Accuracy, int GameId);

public sealed record OwnerStatisticsDto(
    ScoreDto Score, ScoreDto White, ScoreDto Black, List<OwnerOpeningScoreDto> Openings, List<RatingPointDto> RatingHistory,
    double? AverageOpponentElo, int RatedOpponentGames, List<AccuracyPointDto> Accuracy);

public static class OwnerStatisticsTestExtensions
{
    public static async Task<OwnerStatisticsDto> OwnerStatisticsAsync(this HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<OwnerStatisticsDto>($"/api/statistics/owner?{query}", AssessmentJson.Options))!;
}
