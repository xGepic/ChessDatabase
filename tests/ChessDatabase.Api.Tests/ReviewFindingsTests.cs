using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

/// <summary>Cases found in the review of tickets 06–10.</summary>
public class ReviewFindingsTests
{
    private const string NoOwnerPgn = """
        [Date "2025.04.12"]
        [White "Kasparow, Garri"]
        [Black "Huber, Thomas"]
        [Result "0-1"]

        1. e4 c5 2. Nf3 0-1
        """;

    private static async Task<HttpClient> OwnerClientAsync(CollectionFactory factory)
    {
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        return client;
    }

    [Fact]
    public async Task A_resolved_game_is_a_duplicate_when_the_same_file_is_imported_again()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.ImportTextAsync(NoOwnerPgn);
        await client.ResolveAsync(Assert.Single(await client.ReviewListAsync()).Id, new { ownerColour = "white", result = "0-1" });

        var again = await client.ImportTextAsync(NoOwnerPgn);

        Assert.Equal("duplicate", Assert.Single(again.Games).Status);
        Assert.Empty(await client.ReviewListAsync());
    }

    [Fact]
    public async Task Resolving_into_a_duplicate_is_refused_unless_added_anyway()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.ImportTextAsync(NoOwnerPgn);
        await client.ResolveAsync(Assert.Single(await client.ReviewListAsync()).Id, new { ownerColour = "white", result = "0-1" });
        await client.ImportTextAsync(NoOwnerPgn, allowDuplicates: true);
        var item = Assert.Single(await client.ReviewListAsync());

        var refused = await client.PostAsJsonAsync($"/api/import/review/{item.Id}/resolve", new { ownerColour = "white", result = "0-1" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Single(await client.ListGamesAsync());

        await client.ResolveAsync(item.Id, new { ownerColour = "white", result = "0-1", allowDuplicate = true });
        Assert.Equal(2, (await client.ListGamesAsync()).Count);
    }

    [Fact]
    public async Task Multi_line_comments_survive_export_and_reimport()
    {
        using var source = new CollectionFactory();
        var client = await OwnerClientAsync(source);
        var game = await client.CreateGameAsync();
        var e4 = (await client.PlayMainLineAsync(game.Id, "e4", "e5")).MainLine()[0].Id;
        await client.SetCommentAsync(game.Id, e4, "First line.\nSecond line.\n\nNew paragraph.");
        var pgn = await client.GetStringAsync($"/api/games/{game.Id}/pgn");

        using var target = new CollectionFactory();
        var targetClient = await OwnerClientAsync(target);
        var copy = await targetClient.GetGameAsync(Assert.Single((await targetClient.ImportTextAsync(pgn)).Games).GameId!.Value);

        Assert.Equal("First line.\nSecond line.\n\nNew paragraph.", copy.MainLine()[0].CommentAfter);
    }

    [Fact]
    public async Task Line_breaks_inside_comments_of_other_programs_are_only_wrapping()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);

        var summary = await client.ImportTextAsync("[White \"Mustermann, Max\"]\n[Black \"X\"]\n[Result \"1-0\"]\n\n1. e4 {a long comment\nwrapped by the program} 1-0");

        var game = await client.GetGameAsync(Assert.Single(summary.Games).GameId!.Value);
        Assert.Equal("a long comment wrapped by the program", game.MainLine()[0].CommentAfter);
    }

    [Fact]
    public async Task A_comment_with_a_closing_brace_is_refused_because_pgn_cannot_hold_it()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        var game = await client.CreateGameAsync();
        var e4 = (await client.PlayMainLineAsync(game.Id, "e4")).MainLine()[0].Id;

        var response = await client.PutAsJsonAsync($"/api/games/{game.Id}/moves/{e4}/comment", new { text = "set {x}" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("ecoFrom=B&ecoTo=B", "B90")]
    [InlineData("ecoFrom=B9&ecoTo=B9", "B90")]
    [InlineData("ecoTo=b", "B90|A20")]
    public async Task Short_eco_bounds_cover_their_whole_range(string query, string expected)
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.ImportTextAsync(string.Join("\n\n",
            "[Date \"2025.03.01\"]\n[White \"Mustermann, Max\"]\n[Black \"A\"]\n[Result \"1-0\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 1-0",
            "[Date \"2025.02.01\"]\n[White \"Mustermann, Max\"]\n[Black \"B\"]\n[Result \"1-0\"]\n\n1. c4 e5 1-0",
            "[Date \"2025.01.01\"]\n[White \"Mustermann, Max\"]\n[Black \"C\"]\n[Result \"1-0\"]\n\n1. e4 e6 1-0"));

        var games = (await client.GetFromJsonAsync<List<GameSummaryDto>>($"/api/games?{query}"))!;

        Assert.Equal(expected.Split('|'), games.Select(g => g.Eco));
    }

    [Fact]
    public async Task An_eco_bound_that_is_not_a_code_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/games?ecoFrom=Z1")).StatusCode);
    }

    [Theory]
    [InlineData("[White \"Mustermann, Max\"]\n[Black \"?\"]")]
    [InlineData("[White \"Mustermann, Max\"]\n[Black \"\"]")]
    [InlineData("[White \"Mustermann, Max\"]")]
    public async Task A_game_without_the_opponents_name_waits_for_review_without_creating_a_player(string players)
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);

        var summary = await client.ImportTextAsync($"{players}\n[Result \"1-0\"]\n\n1. e4 1-0");

        var game = Assert.Single(summary.Games);
        Assert.Equal("needsReview", game.Status);
        Assert.Contains("name", game.Reason);
        Assert.Empty(await client.GetFromJsonAsync<List<PlayerDto>>("/api/players") ?? []);
        Assert.Null(Assert.Single(await client.ReviewListAsync()).Black);
    }

    [Fact]
    public async Task A_name_differing_only_in_case_is_suggested_not_linked()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        var huber = await client.CreatePlayerAsync("Huber, Thomas");

        var summary = await client.ImportTextAsync("[White \"Mustermann, Max\"]\n[Black \"HUBER, THOMAS\"]\n[Result \"1-0\"]\n\n1. e4 1-0");

        Assert.Equal("needsReview", Assert.Single(summary.Games).Status);
        Assert.Equal(huber.Id, Assert.Single(await client.ReviewListAsync()).BlackSuggestion!.Id);
    }

    [Fact]
    public async Task No_suggestion_is_offered_for_the_owners_own_side()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.CreatePlayerAsync("Mustermann, Moritz");
        await client.CreatePlayerAsync("Huber, Thomas");

        await client.ImportTextAsync("[White \"Mustermann M.\"]\n[Black \"Huber T.\"]\n[Result \"1-0\"]\n\n1. e4 1-0");

        var item = Assert.Single(await client.ReviewListAsync());
        Assert.Equal("white", item.OwnerColour);
        Assert.Null(item.WhiteSuggestion);
        Assert.NotNull(item.BlackSuggestion);
    }
}
