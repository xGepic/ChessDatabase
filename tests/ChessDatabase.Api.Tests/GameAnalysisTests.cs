using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;

namespace ChessDatabase.Api.Tests;

public class GameAnalysisTests
{
    private static readonly string[] Moves = ["e2e4", "e7e5", "d1h5", "b8c6", "f1c4", "g8f6", "h5f7"];

    /// <summary>
    /// 1.e4 e5 2.Qh5 Nc6 3.Bc4 Nf6?? 4.Qxf7#. Scores as the Engine gives them: from the side to move.
    /// 2.Qh5 drops White from +0.30 to −0.40 (an inaccuracy); 3...Nf6 walks into mate (a blunder).
    /// </summary>
    public static FakeEngine ScholarsMateEngine(int lineDelayMs = 0) => new FakeEngine { Name = "Fake Engine 3.1", LineDelayMs = lineDelayMs }
        .Answer([], "e2e4", "depth 20 multipv 1 score cp 30 pv e2e4 e7e5")
        .Answer(Moves[..1], "e7e5", "depth 20 multipv 1 score cp -30 pv e7e5 g1f3")
        .Answer(Moves[..2], "g1f3", "depth 18 multipv 1 score cp 10 pv b1c3", "depth 20 multipv 1 score cp 30 pv g1f3 b8c6")
        .Answer(Moves[..3], "b8c6", "depth 20 multipv 1 score cp 40 pv b8c6 f1c4")
        .Answer(Moves[..4], "f1c4", "depth 20 multipv 1 score cp -40 pv f1c4 g7g6")
        .Answer(Moves[..5], "g7g6", "depth 20 multipv 1 score cp 30 pv g7g6 h5f3 g8f6")
        .Answer(Moves[..6], "h5f7", "depth 20 multipv 1 score mate 1 pv h5f7");

    [Fact]
    public async Task Game_Analysis_produces_the_exact_expected_Engine_Assessment()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = ScholarsMateEngine();
        await client.UseAsync(engine, threads: 2, hashMb: 64);
        var game = await CreateScholarsMateAsync(client);

        (await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { })).EnsureSuccessStatusCode();
        var assessment = await client.WaitForAssessmentAsync(game.Id);

        Assert.Equal("Fake Engine 3.1", assessment.EngineName);
        Assert.Equal((2, 64, 2.0), (assessment.Threads, assessment.HashMb, assessment.SecondsPerMove));
        Assert.False(assessment.Stale);
        Assert.Equal((88.0, 20.0), (assessment.WhiteAccuracy, assessment.BlackAccuracy));

        Assert.Equal([30, 30, 30, -40, -40, -30, null, null], assessment.Positions.Select(p => p.Cp));
        Assert.Equal([null, null, null, null, null, null, 1, 0], assessment.Positions.Select(p => p.Mate));
        Assert.Equal([52.8, 52.8, 52.8, 46.3, 46.3, 47.2, 97.5, 97.5], assessment.Positions.Select(p => p.WhitePercent));

        Assert.Equal(["e4", "e5", "Qh5", "Nc6", "Bc4", "Nf6", "Qxf7#"], assessment.Moves.Select(m => m.San));
        Assert.Equal([null, null, "inaccuracy", null, null, "blunder", null], assessment.Moves.Select(m => m.Classification));
        Assert.Equal([0, 0, 12.9, 0, 0, 100.6, 0], assessment.Moves.Select(m => m.Loss));
        var qh5 = assessment.Moves[2];
        Assert.Equal("Nf3", qh5.BestSan);
        Assert.Equal(["Nf3", "Nc6"], qh5.BestLineSan);
        var nf6 = assessment.Moves[5];
        Assert.Equal(["g6", "Qf3", "Nf6"], nf6.BestLineSan);
        Assert.Equal(["g7g6", "h5f3", "g8f6"], nf6.BestLineUci);
        Assert.Equal(game.MainLine().Select(n => (int?)n.Id), assessment.Moves.Select(m => m.NodeId));

        // The rules settle the final position (checkmate); the Engine is asked about the other seven.
        Assert.Equal(7, engine.Commands().Count(c => c == "go movetime 2000"));
        Assert.Contains("setoption name MultiPV value 1", engine.Commands());
    }

    [Fact]
    public async Task The_Engine_Assessment_is_kept_and_shown_again_without_reanalysis()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = ScholarsMateEngine();
        await client.UseAsync(engine);
        var game = await CreateScholarsMateAsync(client);
        await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { });
        var first = await client.WaitForAssessmentAsync(game.Id);
        var searches = engine.Commands().Count(c => c.StartsWith("go "));

        var again = await client.GetFromJsonAsync<AssessmentDto>($"/api/games/{game.Id}/assessment", AssessmentJson.Options);

        Assert.Equal(first.AnalysedAt, again!.AnalysedAt);
        Assert.Equal(searches, engine.Commands().Count(c => c.StartsWith("go ")));
    }

    [Fact]
    public async Task Analysis_leaves_the_move_tree_and_the_Owners_Annotations_alone()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = ScholarsMateEngine();
        await client.UseAsync(engine);
        var game = await CreateScholarsMateAsync(client);
        var nf6 = game.MainLine()[5];
        await client.SetNagsAsync(game.Id, nf6.Id, 1);
        await client.SetCommentAsync(game.Id, nf6.Id, "Developing.");
        await client.PlayAsync(game.Id, game.MainLine()[4].Id, "g6");
        var before = JsonSerializer.Serialize(await client.GetGameAsync(game.Id));

        await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { });
        await client.WaitForAssessmentAsync(game.Id);

        Assert.Equal(before, JsonSerializer.Serialize(await client.GetGameAsync(game.Id)));
    }

    [Fact]
    public async Task Analysis_runs_in_the_background_with_progress_while_the_app_keeps_answering()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = ScholarsMateEngine(lineDelayMs: 150);
        await client.UseAsync(engine);
        var game = await CreateScholarsMateAsync(client);
        await using var hub = await factory.ConnectAnalysisAsync();
        var progress = new List<JsonElement>();
        hub.Connection.On<JsonElement>("analysisQueue", s => { lock (progress) progress.Add(s); });

        var started = await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { });
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);

        await Wait.UntilAsync(() => client.GetFromJsonAsync<JsonElement>("/api/analysis/queue").Result
            .GetProperty("running") is { ValueKind: JsonValueKind.Object } r && r.GetProperty("done").GetInt32() > 0);
        Assert.Single(await client.ListGamesAsync()); // the app keeps answering meanwhile
        await client.WaitForAssessmentAsync(game.Id);
        lock (progress)
            Assert.Contains(progress, s => s.GetProperty("running") is { ValueKind: JsonValueKind.Object } r
                && r.GetProperty("total").GetInt32() == 8 && r.GetProperty("done").GetInt32() is > 0 and < 8);
    }

    [Fact]
    public async Task Without_a_configured_Engine_analysis_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync();

        var response = await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Set the Stockfish path in Settings", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_changed_main_line_marks_the_Assessment_as_stale()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = ScholarsMateEngine();
        await client.UseAsync(engine);
        var game = await CreateScholarsMateAsync(client);
        await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { });
        await client.WaitForAssessmentAsync(game.Id);

        var g6 = (await client.PlayAsync(game.Id, game.MainLine()[4].Id, "g6")).NodeId;
        await client.PostAsync($"/api/games/{game.Id}/moves/{g6}/promote", null);

        var assessment = await client.GetFromJsonAsync<AssessmentDto>($"/api/games/{game.Id}/assessment", AssessmentJson.Options);
        Assert.True(assessment!.Stale);
        Assert.Equal(5, assessment.Moves.Count(m => m.NodeId is not null));
    }

    public static async Task<GameDto> CreateScholarsMateAsync(HttpClient client)
    {
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync();
        return await client.PlayMainLineAsync(game.Id, Moves);
    }
}

public sealed record AssessedPositionDto(int Ply, double WhitePercent, int? Cp, int? Mate);

public sealed record AssessedMoveDto(
    int Ply, int? NodeId, string San, string? Classification, double Loss, double Accuracy,
    string? BestSan, List<string> BestLineSan, List<string> BestLineUci);

public sealed record AssessmentDto(
    int GameId, string EngineName, int Threads, int HashMb, double SecondsPerMove, DateTime AnalysedAt, bool Stale,
    double? WhiteAccuracy, double? BlackAccuracy, List<AssessedPositionDto> Positions, List<AssessedMoveDto> Moves);

public static class AssessmentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class AnalysisTestExtensions
{
    public static async Task<AssessmentDto> WaitForAssessmentAsync(this HttpClient client, int gameId, DateTime? after = null)
    {
        for (var i = 0; i < 200; i++)
        {
            var response = await client.GetAsync($"/api/games/{gameId}/assessment");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var dto = (await response.Content.ReadFromJsonAsync<AssessmentDto>(AssessmentJson.Options))!;
                if (after is null || dto.AnalysedAt > after) return dto;
            }
            await Task.Delay(50);
        }
        var queue = await client.GetStringAsync("/api/analysis/queue");
        throw new TimeoutException($"No Engine Assessment for Game {gameId}. Queue: {queue}");
    }
}
