using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class ImportReviewTests
{
    private const string NoOwnerPgn = """
        [Event "Vienna Open"]
        [Date "2025.04.12"]
        [White "Kasparow, Garri"]
        [Black "Huber, Thomas"]
        [Result "0-1"]
        [WhiteElo "2100"]

        1. e4 c5 2. Nf3 0-1
        """;

    private const string NoResultPgn = """
        [White "Mustermann, Max"]
        [Black "Berger, Anna"]
        [Result "*"]

        1. d4 d5 *
        """;

    private const string CloseOpponentPgn = """
        [Date "2025.06.01"]
        [White "Mustermann, Max"]
        [Black "Huber T."]
        [Result "1-0"]

        1. e4 e5 1-0
        """;

    private static async Task<HttpClient> OwnerClientAsync(CollectionFactory factory)
    {
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        return client;
    }

    [Fact]
    public async Task Held_back_games_are_listed_with_what_they_need()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.ImportTextAsync(NoOwnerPgn + "\n\n" + NoResultPgn);

        var items = await client.ReviewListAsync();

        Assert.Equal(2, items.Count);
        var noOwner = items.Single(i => i.White == "Kasparow, Garri");
        Assert.Null(noOwner.OwnerColour);
        Assert.Equal("0-1", noOwner.Result);
        var noResult = items.Single(i => i.Black == "Berger, Anna");
        Assert.Equal("white", noResult.OwnerColour);
        Assert.Null(noResult.Result);
    }

    [Fact]
    public async Task Choosing_the_owners_side_moves_the_game_into_the_collection()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.ImportTextAsync(NoOwnerPgn);
        var item = Assert.Single(await client.ReviewListAsync());

        var game = await client.ResolveAsync(item.Id, new { ownerColour = "white", result = item.Result });

        Assert.Equal(("white", "Huber, Thomas", 2100, "0-1"), (game.OwnerColour, game.Opponent.Name, game.WhiteElo, game.Result));
        Assert.Equal(["e4", "c5", "Nf3"], game.MainLine().Select(n => n.San));
        Assert.Equal("Vienna Open", game.Event!.Name);
        Assert.Empty(await client.ReviewListAsync());
        Assert.Single(await client.ListGamesAsync());
    }

    [Fact]
    public async Task Setting_the_missing_result_moves_the_game_into_the_collection()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.ImportTextAsync(NoResultPgn);
        var item = Assert.Single(await client.ReviewListAsync());

        var game = await client.ResolveAsync(item.Id, new { ownerColour = item.OwnerColour, result = "1/2-1/2" });

        Assert.Equal(("1/2-1/2", "Berger, Anna"), (game.Result, game.Opponent.Name));
    }

    [Fact]
    public async Task Resolving_without_a_side_or_result_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.ImportTextAsync(NoOwnerPgn + "\n\n" + NoResultPgn);
        var items = await client.ReviewListAsync();

        var noSide = await client.PostAsJsonAsync($"/api/import/review/{items[0].Id}/resolve", new { result = "1-0" });
        var noResult = await client.PostAsJsonAsync($"/api/import/review/{items[1].Id}/resolve", new { ownerColour = "white", result = "*" });

        Assert.Equal(HttpStatusCode.BadRequest, noSide.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noResult.StatusCode);
        Assert.Equal(2, (await client.ReviewListAsync()).Count);
    }

    [Fact]
    public async Task A_close_opponent_name_is_suggested_and_never_linked_without_confirmation()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        var huber = await client.CreatePlayerAsync("Huber, Thomas");

        var summary = await client.ImportTextAsync(CloseOpponentPgn);

        Assert.Equal("needsReview", Assert.Single(summary.Games).Status);
        Assert.Contains("Huber, Thomas", summary.Games[0].Reason);
        Assert.Empty(await client.ListGamesAsync());
        var item = Assert.Single(await client.ReviewListAsync());
        Assert.Equal(huber.Id, item.BlackSuggestion!.Id);
        Assert.Null(item.WhiteSuggestion);
    }

    [Fact]
    public async Task Confirming_the_suggestion_links_the_existing_player()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        var huber = await client.CreatePlayerAsync("Huber, Thomas");
        await client.ImportTextAsync(CloseOpponentPgn);
        var item = Assert.Single(await client.ReviewListAsync());

        var game = await client.ResolveAsync(item.Id, new { ownerColour = "white", result = "1-0", opponentPlayerId = huber.Id });

        Assert.Equal(huber.Id, game.Opponent.Id);
        Assert.Single((await client.GetFromJsonAsync<List<PlayerDto>>("/api/players"))!);
    }

    [Fact]
    public async Task Rejecting_the_suggestion_creates_a_new_player()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        var huber = await client.CreatePlayerAsync("Huber, Thomas");
        await client.ImportTextAsync(CloseOpponentPgn);
        var item = Assert.Single(await client.ReviewListAsync());

        var game = await client.ResolveAsync(item.Id, new { ownerColour = "white", result = "1-0", opponentPlayerId = (int?)null });

        Assert.NotEqual(huber.Id, game.Opponent.Id);
        Assert.Equal("Huber T.", game.Opponent.Name);
    }

    [Fact]
    public async Task Linking_to_a_player_with_an_unrelated_name_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.CreatePlayerAsync("Huber, Thomas");
        var berger = await client.CreatePlayerAsync("Berger, Anna");
        await client.ImportTextAsync(CloseOpponentPgn);
        var item = Assert.Single(await client.ReviewListAsync());

        var response = await client.PostAsJsonAsync($"/api/import/review/{item.Id}/resolve",
            new { ownerColour = "white", result = "1-0", opponentPlayerId = berger.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_held_back_game_can_be_discarded()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.ImportTextAsync(NoOwnerPgn);
        var item = Assert.Single(await client.ReviewListAsync());

        var response = await client.DeleteAsync($"/api/import/review/{item.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await client.ReviewListAsync());
        Assert.Empty(await client.ListGamesAsync());
    }

    [Fact]
    public async Task Importing_the_same_file_twice_flags_every_game_as_a_duplicate_and_adds_nothing()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        var pgn = string.Join("\n\n", NoOwnerPgn, NoResultPgn,
            "[White \"Mustermann, Max\"]\n[Black \"Berger, Anna\"]\n[Date \"2025.01.10\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Nf3 1-0",
            "[White \"Schuster, Paul\"]\n[Black \"Mustermann, Max\"]\n[Result \"0-1\"]\n\n1. c4 0-1");
        var first = await client.ImportTextAsync(pgn);
        Assert.Equal((2, 2, 0), (first.Imported, first.NeedsReview, first.Duplicates));

        var second = await client.ImportTextAsync(pgn);

        Assert.Equal((0, 0, 4, 0), (second.Imported, second.NeedsReview, second.Duplicates, second.Failed));
        Assert.All(second.Games, g => Assert.Equal("duplicate", g.Status));
        Assert.Equal(2, (await client.ListGamesAsync()).Count);
        Assert.Equal(2, (await client.ReviewListAsync()).Count);
    }

    [Fact]
    public async Task A_duplicate_can_be_imported_anyway()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        const string pgn = "[White \"Mustermann, Max\"]\n[Black \"Berger, Anna\"]\n[Result \"1-0\"]\n\n1. e4 e5 1-0";
        await client.ImportTextAsync(pgn);
        var duplicate = Assert.Single((await client.ImportTextAsync(pgn)).Games);
        Assert.Equal("duplicate", duplicate.Status);
        Assert.NotNull(duplicate.GameId);

        var response = await client.PostAsJsonAsync("/api/import/text", new { pgn = duplicate.Pgn, allowDuplicates = true });

        var summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        Assert.Equal(1, summary.Imported);
        Assert.Equal(2, (await client.ListGamesAsync()).Count);
    }

    [Fact]
    public async Task The_same_moves_against_a_different_opponent_or_date_are_not_duplicates()
    {
        using var factory = new CollectionFactory();
        var client = await OwnerClientAsync(factory);
        await client.ImportTextAsync("[White \"Mustermann, Max\"]\n[Black \"Berger, Anna\"]\n[Date \"2025.01.10\"]\n[Result \"1-0\"]\n\n1. e4 e5 1-0");

        var summary = await client.ImportTextAsync(string.Join("\n\n",
            "[White \"Mustermann, Max\"]\n[Black \"Berger, Anna\"]\n[Date \"2025.01.11\"]\n[Result \"1-0\"]\n\n1. e4 e5 1-0",
            "[White \"Mustermann, Max\"]\n[Black \"Schuster, Paul\"]\n[Date \"2025.01.10\"]\n[Result \"1-0\"]\n\n1. e4 e5 1-0",
            "[White \"Mustermann, Max\"]\n[Black \"Berger, Anna\"]\n[Date \"2025.01.10\"]\n[Result \"1-0\"]\n\n1. e4 c5 1-0"));

        Assert.Equal((3, 0), (summary.Imported, summary.Duplicates));
    }
}

public sealed record ReviewItemDto(
    int Id, string? White, string? Black, string? Date, string Reason, string? OwnerColour, string? Result,
    PlayerDto? WhiteSuggestion, PlayerDto? BlackSuggestion);
