using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace ChessDatabase.Api.Tests;

public class AnalysisQueueTests
{
    [Fact]
    public async Task Queued_Games_are_analysed_one_after_another_in_order()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = new FakeEngine { LineDelayMs = 20 };
        await client.UseAsync(engine);
        var games = await CreateGamesAsync(client, 3);
        await using var hub = await factory.ConnectAnalysisAsync();
        var finished = new List<int>();
        hub.Connection.On<JsonElement>("analysisFinished", e => { lock (finished) finished.Add(e.GetProperty("gameId").GetInt32()); });
        int[] order = [games[2].Id, games[0].Id, games[1].Id];

        var response = await client.PostAsJsonAsync("/api/analysis/queue", new { gameIds = order });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<QueueDto>(AssessmentJson.Options);
        Assert.Equal(3, status!.BatchTotal);
        await Wait.UntilAsync(() => { lock (finished) return finished.Count == 3; });
        lock (finished) Assert.Equal(order, finished);
        Assert.Equal(new QueueDto(null, [], 0, 0), await client.GetFromJsonAsync<QueueDto>("/api/analysis/queue", AssessmentJson.Options));
    }

    [Fact]
    public async Task Overall_progress_counts_the_Games_of_the_batch()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = new FakeEngine { LineDelayMs = 100 };
        await client.UseAsync(engine);
        var games = await CreateGamesAsync(client, 2);

        await client.PostAsJsonAsync("/api/analysis/queue", new { gameIds = games.Select(g => g.Id) });

        QueueDto? seen = null;
        await Wait.UntilAsync(() =>
        {
            seen = client.GetFromJsonAsync<QueueDto>("/api/analysis/queue", AssessmentJson.Options).Result;
            return seen!.BatchDone == 1;
        });
        Assert.Equal(2, seen!.BatchTotal);
        Assert.Equal(games[1].Id, seen.Running?.GameId);
        Assert.Empty(seen.Queued);
    }

    [Fact]
    public async Task Cancelling_empties_the_queue_and_keeps_finished_Engine_Assessments()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = new FakeEngine { LineDelayMs = 150 };
        await client.UseAsync(engine);
        var games = await CreateGamesAsync(client, 3);
        await client.PostAsJsonAsync("/api/analysis/queue", new { gameIds = games.Select(g => g.Id) });
        await client.WaitForAssessmentAsync(games[0].Id);

        var cancelled = await client.DeleteAsync("/api/analysis/queue");

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        await Wait.UntilAsync(() => client.GetFromJsonAsync<QueueDto>("/api/analysis/queue", AssessmentJson.Options).Result!.Running is null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/games/{games[0].Id}/assessment")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/games/{games[1].Id}/assessment")).StatusCode);
        await Task.Delay(500);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/games/{games[2].Id}/assessment")).StatusCode);
    }

    [Fact]
    public async Task Re_running_replaces_the_Engine_Assessment_and_records_the_new_engine_and_settings()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var first = new FakeEngine { Name = "Fake Engine 3.1" };
        await client.UseAsync(first, threads: 4, hashMb: 128);
        var game = (await CreateGamesAsync(client, 1))[0];
        await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { });
        var before = await client.WaitForAssessmentAsync(game.Id);

        using var second = new FakeEngine { Name = "Fake Engine 4.0" };
        await client.UseAsync(second, threads: 8, hashMb: 512);
        await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { secondsPerMove = 0.5 });
        var after = await client.WaitForAssessmentAsync(game.Id, after: before.AnalysedAt);

        Assert.Equal("Fake Engine 4.0", after.EngineName);
        Assert.Equal((8, 512, 0.5), (after.Threads, after.HashMb, after.SecondsPerMove));
        Assert.Contains("go depth 20 movetime 500", second.Commands());
    }

    [Fact]
    public async Task A_Game_already_waiting_or_running_is_not_queued_twice()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = new FakeEngine { LineDelayMs = 100 };
        await client.UseAsync(engine);
        var games = await CreateGamesAsync(client, 2);

        await client.PostAsJsonAsync("/api/analysis/queue", new { gameIds = games.Select(g => g.Id) });
        var again = await (await client.PostAsJsonAsync("/api/analysis/queue", new { gameIds = games.Select(g => g.Id) }))
            .Content.ReadFromJsonAsync<QueueDto>(AssessmentJson.Options);

        Assert.Equal(2, again!.BatchTotal);
    }

    [Fact]
    public async Task Unknown_Games_and_an_empty_selection_are_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = new FakeEngine();
        await client.UseAsync(engine);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/analysis/queue", new { gameIds = Array.Empty<int>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/analysis/queue", new { gameIds = new[] { 999 } })).StatusCode);
    }

    [Fact]
    public async Task The_game_list_shows_and_filters_which_Games_are_analysed()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var engine = new FakeEngine();
        await client.UseAsync(engine);
        var games = await CreateGamesAsync(client, 2);
        await client.PostAsJsonAsync($"/api/games/{games[0].Id}/analysis", new { });
        await client.WaitForAssessmentAsync(games[0].Id);

        var list = await client.GetFromJsonAsync<List<JsonElement>>("/api/games");
        Assert.True(list!.Single(g => g.GetProperty("id").GetInt32() == games[0].Id).GetProperty("analysed").GetBoolean());
        Assert.False(list!.Single(g => g.GetProperty("id").GetInt32() == games[1].Id).GetProperty("analysed").GetBoolean());

        var analysed = await client.GetFromJsonAsync<List<JsonElement>>("/api/games?analysed=true");
        var notAnalysed = await client.GetFromJsonAsync<List<JsonElement>>("/api/games?analysed=false");
        Assert.Equal([games[0].Id], analysed!.Select(g => g.GetProperty("id").GetInt32()));
        Assert.Equal([games[1].Id], notAnalysed!.Select(g => g.GetProperty("id").GetInt32()));
    }

    private static async Task<List<GameDto>> CreateGamesAsync(HttpClient client, int count)
    {
        await client.SetOwnerNameAsync("Max Mustermann");
        var games = new List<GameDto>();
        for (var i = 0; i < count; i++)
        {
            var game = await client.CreateGameAsync(opponent: $"Opponent {i}");
            games.Add(await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3", "Nc6"));
        }
        return games;
    }
}

public sealed record RunningDto(int GameId, int Done, int Total);

public sealed record QueueDto(RunningDto? Running, List<int> Queued, int BatchDone, int BatchTotal)
{
    public bool Equals(QueueDto? other) =>
        other is not null && Running == other.Running && Queued.SequenceEqual(other.Queued)
        && BatchDone == other.BatchDone && BatchTotal == other.BatchTotal;

    public override int GetHashCode() => HashCode.Combine(Running, BatchDone, BatchTotal);
}
