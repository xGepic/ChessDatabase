using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Time.Testing;

namespace ChessDatabase.Api.Tests;

/// <summary>
/// Mistake Training: the Owner's mistakes and blunders from Game Analysis become positions to solve,
/// scheduled by spaced repetition. The Game is Scholar's Mate (see <see cref="GameAnalysisTests"/>):
/// 2.Qh5 is White's inaccuracy, 3...Nf6?? Black's blunder.
/// </summary>
public class MistakeTrainingTests
{
    private static readonly DateTimeOffset Morning = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly string[] Moves = ["e2e4", "e7e5", "d1h5", "b8c6", "f1c4", "g8f6", "h5f7"];

    /// <summary>The Scholar's Mate script, plus the Engine's verdict after two other answers to 3.Bc4.</summary>
    private static FakeEngine Engine() => GameAnalysisTests.ScholarsMateEngine()
        .Answer([.. Moves[..5], "d8e7"], "g1f3", "depth 20 multipv 1 score cp -25 pv g1f3 g8f6") // Qe7 guards f7: Black still +0.25
        .Answer([.. Moves[..5], "a7a6"], "h5f7", "depth 20 multipv 1 score mate 1 pv h5f7");     // a6?? allows mate too

    [Fact]
    public async Task Only_the_Owners_own_mistakes_and_blunders_become_items()
    {
        using var factory = new CollectionFactory(clock: new FakeTimeProvider(Morning));
        var client = factory.CreateClient();
        using var engine = Engine();
        await client.UseAsync(engine);
        var asBlack = await AnalysedScholarsMateAsync(client, "black");  // the Owner played 3...Nf6??
        await AnalysedScholarsMateAsync(client, "white");                 // the Owner's 2.Qh5 is only an inaccuracy

        Assert.Equal((1, 1), await client.MistakesDueAsync());
        var item = (await client.NextMistakeAsync())!;
        Assert.Equal((asBlack.Id, 6, "black", "Nf6", "blunder"), (item.GameId, item.Ply, item.Side, item.PlayedSan, item.Classification));
        Assert.Equal(FakeEngine.Key(Moves[..5]), string.Join(' ', item.Fen.Split(' ').Take(4)));
        Assert.Equal("f1c4", item.LastMove);
        Assert.Equal("Opponent black", item.Opponent);
    }

    [Fact]
    public async Task The_best_move_and_moves_within_the_margin_are_right_and_the_best_line_is_shown()
    {
        using var factory = new CollectionFactory(clock: new FakeTimeProvider(Morning));
        var client = factory.CreateClient();
        using var engine = Engine();
        await client.UseAsync(engine);
        await AnalysedScholarsMateAsync(client, "black");
        var itemId = (await client.NextMistakeAsync())!.ItemId;
        var asked = engine.Commands().Count(c => c.StartsWith("go ", StringComparison.Ordinal));

        var best = await client.AnswerMistakeAsync(itemId, "g6");
        var qe7 = await client.AnswerMistakeAsync(itemId, "Qe7");     // loses 0.05: within the margin
        var a6 = await client.AnswerMistakeAsync(itemId, "a7a6");      // walks into mate
        var again = await client.AnswerMistakeAsync(itemId, "Nf6");    // the Game's blunder

        Assert.Equal((true, true, false, false), (best.Correct, qe7.Correct, a6.Correct, again.Correct));
        Assert.Equal(["g6", "Qf3", "Nf6"], best.BestLineSan);
        Assert.Equal(("g7g6", "g7g6"), (a6.BestLineUci[0], a6.Shown)); // a wrong answer shows the Engine's move
        Assert.Equal("d8e7", qe7.Shown);
        // Only the two answers that were neither the best move nor the Game's own move needed the Engine.
        Assert.Equal(asked + 2, engine.Commands().Count(c => c.StartsWith("go ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Items_follow_spaced_repetition()
    {
        var clock = new FakeTimeProvider(Morning);
        using var factory = new CollectionFactory(clock: clock);
        var client = factory.CreateClient();
        using var engine = Engine();
        await client.UseAsync(engine);
        await AnalysedScholarsMateAsync(client, "black");
        var itemId = (await client.NextMistakeAsync())!.ItemId;

        Assert.Equal(Morning.AddMinutes(1), (await client.AnswerMistakeAsync(itemId, "a6")).DueAt);
        Assert.Equal(Morning.AddDays(1), (await client.AnswerMistakeAsync(itemId, "g6")).DueAt);
        Assert.Null(await client.NextMistakeAsync());
        Assert.Equal((0, 1), await client.MistakesDueAsync());
    }

    [Fact]
    public async Task Re_running_Game_Analysis_keeps_the_progress_of_unchanged_positions_and_drops_the_rest()
    {
        var clock = new FakeTimeProvider(Morning);
        using var factory = new CollectionFactory(clock: clock);
        var client = factory.CreateClient();
        using var engine = Engine();
        await client.UseAsync(engine);
        var game = await AnalysedScholarsMateAsync(client, "black");
        var itemId = (await client.NextMistakeAsync())!.ItemId;
        await client.AnswerMistakeAsync(itemId, "g6"); // due tomorrow

        await ReanalyseAsync(client, game.Id, clock);
        Assert.Equal((0, 1), await client.MistakesDueAsync()); // the same blunder, its progress kept

        // A different Engine no longer minds 3...Nf6: the item goes.
        using var kinder = new FakeEngine()
            .Answer([], "e2e4", "depth 20 multipv 1 score cp 30 pv e2e4")
            .Answer(Moves[..1], "e7e5", "depth 20 multipv 1 score cp -30 pv e7e5")
            .Answer(Moves[..2], "g1f3", "depth 20 multipv 1 score cp 30 pv g1f3")
            .Answer(Moves[..3], "b8c6", "depth 20 multipv 1 score cp 30 pv b8c6")
            .Answer(Moves[..4], "f1c4", "depth 20 multipv 1 score cp -30 pv f1c4")
            .Answer(Moves[..5], "g8f6", "depth 20 multipv 1 score cp 30 pv g8f6")
            .Answer(Moves[..6], "h5f7", "depth 20 multipv 1 score cp -30 pv d2d3");
        await client.UseAsync(kinder);
        await ReanalyseAsync(client, game.Id, clock);
        Assert.Equal((0, 0), await client.MistakesDueAsync());
    }

    [Fact]
    public async Task Answers_are_checked_by_Chess_Rules_and_unknown_items_are_not_found()
    {
        using var factory = new CollectionFactory(clock: new FakeTimeProvider(Morning));
        var client = factory.CreateClient();
        using var engine = Engine();
        await client.UseAsync(engine);
        var game = await AnalysedScholarsMateAsync(client, "black");
        var itemId = (await client.NextMistakeAsync())!.ItemId;

        var illegal = await client.PostAsJsonAsync($"/api/training/mistakes/{itemId}/answer", new { move = "e5e4" }); // blocked by White's e4
        await client.DeleteAsync($"/api/games/{game.Id}");
        var gone = await client.PostAsJsonAsync($"/api/training/mistakes/{itemId}/answer", new { move = "g6" });

        Assert.Equal(HttpStatusCode.BadRequest, illegal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal((0, 0), await client.MistakesDueAsync());
    }

    [Fact]
    public async Task Without_a_working_Engine_an_answer_that_needs_judging_is_not_counted()
    {
        using var factory = new CollectionFactory(clock: new FakeTimeProvider(Morning));
        var client = factory.CreateClient();
        using var engine = Engine();
        await client.UseAsync(engine);
        await AnalysedScholarsMateAsync(client, "black");
        var itemId = (await client.NextMistakeAsync())!.ItemId;
        engine.Dispose(); // its script is gone: the Engine fails to start

        var response = await client.PostAsJsonAsync($"/api/training/mistakes/{itemId}/answer", new { move = "Qe7" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal((1, 1), await client.MistakesDueAsync());
        Assert.True((await client.NextMistakeAsync())!.New); // still never answered
    }

    private static async Task<GameDto> AnalysedScholarsMateAsync(HttpClient client, string ownerColour)
    {
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync(ownerColour: ownerColour, opponent: $"Opponent {ownerColour}", result: ownerColour == "white" ? "1-0" : "0-1");
        game = await client.PlayMainLineAsync(game.Id, Moves);
        (await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { })).EnsureSuccessStatusCode();
        await client.WaitForAssessmentAsync(game.Id);
        return game;
    }

    private static async Task ReanalyseAsync(HttpClient client, int gameId, FakeTimeProvider clock)
    {
        var before = (await client.WaitForAssessmentAsync(gameId)).AnalysedAt;
        clock.Advance(TimeSpan.FromSeconds(1));
        (await client.PostAsJsonAsync($"/api/games/{gameId}/analysis", new { })).EnsureSuccessStatusCode();
        await client.WaitForAssessmentAsync(gameId, after: before);
    }
}

public sealed record MistakeItemDto(
    int ItemId, int GameId, int Ply, string Side, string Fen, string? LastMove, string PlayedSan, string Classification,
    string Opponent, string? Date, bool New);

public sealed record MistakeAnswerDto(
    bool Correct, List<string> BestLineSan, List<string> BestLineUci, string Shown, string FenAfter, DateTimeOffset DueAt);

public static class MistakeTrainingTestExtensions
{
    public static async Task<(int Due, int Total)> MistakesDueAsync(this HttpClient client)
    {
        var summary = (await client.GetFromJsonAsync<TrainingSummaryDto>("/api/training/mistakes"))!;
        return (summary.Due, summary.Total);
    }

    public static async Task<MistakeItemDto?> NextMistakeAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/training/mistakes/next");
        response.EnsureSuccessStatusCode();
        return response.StatusCode == HttpStatusCode.NoContent ? null : await response.Content.ReadFromJsonAsync<MistakeItemDto>();
    }

    public static async Task<MistakeAnswerDto> AnswerMistakeAsync(this HttpClient client, int itemId, string move)
    {
        var response = await client.PostAsJsonAsync($"/api/training/mistakes/{itemId}/answer", new { move });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MistakeAnswerDto>())!;
    }
}
