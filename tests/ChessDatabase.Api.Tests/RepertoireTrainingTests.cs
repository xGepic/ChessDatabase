using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Time.Testing;

namespace ChessDatabase.Api.Tests;

/// <summary>Repertoire Training: the Owner answers the Opponent's moves with their Repertoire move, scheduled SM-2 style.</summary>
public class RepertoireTrainingTests
{
    private static readonly DateTimeOffset Morning = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_position_where_the_Owner_must_answer_is_an_item_and_new_lines_become_new_items()
    {
        using var factory = new CollectionFactory(clock: new FakeTimeProvider(Morning));
        var client = factory.CreateClient();
        await client.AddRepertoireLineAsync("black", null, "e4", "c5", "Nf3", "d6");
        await client.AddRepertoireLineAsync("black", null, "d4", "Nf6");

        Assert.Equal((3, 3), await client.DueAsync()); // after 1.e4, 1.e4 c5 2.Nf3, and 1.d4

        await client.AddRepertoireLineAsync("white", null, "e4", "e5", "Nf3");
        Assert.Equal((5, 5), await client.DueAsync()); // + the start and 1.e4 e5
    }

    [Fact]
    public async Task A_session_shows_the_position_after_the_Opponents_move_and_checks_the_answer()
    {
        using var factory = new CollectionFactory(clock: new FakeTimeProvider(Morning));
        var client = factory.CreateClient();
        await client.AddRepertoireLineAsync("black", null, "e4", "c5", "Nf3", "d6");

        var first = await client.NextItemAsync();
        Assert.Equal(("black", "e4"), (first!.Side, Assert.Single(first.Line).San));
        Assert.StartsWith("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b", first.Fen);

        var wrong = await client.AnswerAsync(first.ItemId, "e5");
        Assert.False(wrong.Correct);
        Assert.Equal(("c5", "c7c5"), (Assert.Single(wrong.Expected), Assert.Single(wrong.ExpectedUci)));

        var second = await client.NextItemAsync();
        Assert.Equal(["e4", "c5", "Nf3"], second!.Line.Select(m => m.San));
        var right = await client.AnswerAsync(second.ItemId, "d7d6");
        Assert.True(right.Correct);

        // The failed item comes back in the same session; the solved one waits until tomorrow.
        var again = await client.NextItemAsync();
        Assert.Equal(first.ItemId, again!.ItemId);
        await client.AnswerAsync(again.ItemId, "c5");
        Assert.Null(await client.NextItemAsync());
        Assert.Equal((0, 2), await client.DueAsync());
    }

    [Fact]
    public async Task Answers_to_positions_no_longer_in_the_Repertoire_or_illegal_moves_are_refused()
    {
        using var factory = new CollectionFactory(clock: new FakeTimeProvider(Morning));
        var client = factory.CreateClient();
        var line = await client.AddRepertoireLineAsync("black", null, "e4", "c5");
        var item = await client.NextItemAsync();

        var illegal = await client.PostAsJsonAsync($"/api/training/repertoire/{item!.ItemId}/answer", new { move = "Ke2" });
        await client.DeleteAsync($"/api/repertoire/black/nodes/{line.Repertoire.FirstMoves[0]}");
        var gone = await client.PostAsJsonAsync($"/api/training/repertoire/{item.ItemId}/answer", new { move = "c5" });

        Assert.Equal(HttpStatusCode.BadRequest, illegal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal((0, 0), await client.DueAsync());
    }

    [Fact]
    public async Task Due_today_ends_at_local_midnight_also_on_the_day_the_clocks_change()
    {
        // Vienna: on 29 March 2026 the clocks go forward at 02:00, so that day ends at 22:00 UTC, not 23:00.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 28, 22, 30, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna"));
        using var factory = new CollectionFactory(clock: clock);
        var client = factory.CreateClient();
        await client.AddRepertoireLineAsync("black", null, "e4", "c5");
        await client.AnswerAsync((await client.NextItemAsync())!.ItemId, "c5"); // due 29 March 22:30 UTC = 00:30 on the 30th in Vienna

        clock.SetUtcNow(new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero)); // 01:30 in Vienna, before the change

        Assert.Equal((0, 1), await client.DueAsync());
    }

    [Fact]
    public async Task Asking_for_the_summary_and_the_next_item_at_once_adds_each_item_once()
    {
        using var factory = new CollectionFactory(clock: new FakeTimeProvider(Morning));
        var client = factory.CreateClient();
        await client.AddRepertoireLineAsync("white", null, "e4", "e5", "Nf3", "Nc6", "Bb5");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            client.GetAsync(i % 2 == 0 ? "/api/training/repertoire" : "/api/training/repertoire/next")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal((3, 3), await client.DueAsync());
    }

    [Fact]
    public async Task Right_answers_push_the_next_review_out_and_a_wrong_one_starts_the_item_over()
    {
        var clock = new FakeTimeProvider(Morning);
        using var factory = new CollectionFactory(clock: clock);
        var client = factory.CreateClient();
        await client.AddRepertoireLineAsync("black", null, "e4", "c5");
        var itemId = (await client.NextItemAsync())!.ItemId;

        // SM-2 with ease 2.5: 1 day, 6 days, then 6 × 2.5 = 15 days.
        Assert.Equal(Morning.AddDays(1), (await client.AnswerAsync(itemId, "c5")).DueAt);
        Assert.Null(await client.NextItemAsync());
        clock.Advance(TimeSpan.FromHours(23)); // tomorrow 07:00: due later today, so due now
        Assert.Equal((1, 1), await client.DueAsync());
        Assert.Equal(Morning.AddDays(1 + 6).AddHours(-1), (await client.AnswerAsync(itemId, "c5")).DueAt);
        clock.SetUtcNow(Morning.AddDays(7));
        Assert.Equal(Morning.AddDays(7 + 15), (await client.AnswerAsync(itemId, "c5")).DueAt);

        // Wrong: back in a minute, the ease drops to 2.18, and the intervals start again: 1, 6, then 6 × 2.18 ≈ 13.
        clock.SetUtcNow(Morning.AddDays(22));
        Assert.Equal(Morning.AddDays(22).AddMinutes(1), (await client.AnswerAsync(itemId, "e5")).DueAt);
        Assert.Equal(itemId, (await client.NextItemAsync())!.ItemId);
        Assert.Equal(Morning.AddDays(23), (await client.AnswerAsync(itemId, "c5")).DueAt);
        clock.SetUtcNow(Morning.AddDays(23));
        Assert.Equal(Morning.AddDays(29), (await client.AnswerAsync(itemId, "c5")).DueAt);
        clock.SetUtcNow(Morning.AddDays(29));
        Assert.Equal(Morning.AddDays(29 + 13), (await client.AnswerAsync(itemId, "c5")).DueAt);
    }
}

public sealed record TrainingMoveDto(string Uci, string San);

public sealed record RepertoireTrainingItemDto(int ItemId, string Side, string Fen, List<TrainingMoveDto> Line, bool New);

public sealed record TrainingAnswerDto(bool Correct, List<string> Expected, List<string> ExpectedUci, DateTimeOffset DueAt);

public sealed record TrainingSummaryDto(int Due, int Total);

public static class RepertoireTrainingTestExtensions
{
    public static async Task<(int Due, int Total)> DueAsync(this HttpClient client)
    {
        var summary = (await client.GetFromJsonAsync<TrainingSummaryDto>("/api/training/repertoire"))!;
        return (summary.Due, summary.Total);
    }

    public static async Task<RepertoireTrainingItemDto?> NextItemAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/training/repertoire/next");
        response.EnsureSuccessStatusCode();
        return response.StatusCode == HttpStatusCode.NoContent ? null : await response.Content.ReadFromJsonAsync<RepertoireTrainingItemDto>();
    }

    public static async Task<TrainingAnswerDto> AnswerAsync(this HttpClient client, int itemId, string move)
    {
        var response = await client.PostAsJsonAsync($"/api/training/repertoire/{itemId}/answer", new { move });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TrainingAnswerDto>())!;
    }
}
