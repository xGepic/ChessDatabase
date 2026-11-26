using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace ChessDatabase.Api.Tests;

public class ImportTests
{
    /// <summary>
    /// One file with every case the import has to handle: Variations, comments and symbols,
    /// German piece letters, the Owner's name in different forms and through an Alias, missing and
    /// zero Elos, a malformed Game, a Game without the Owner and a Game without a result.
    /// </summary>
    private const string FixturePgn = """
        [Event "Club Championship"]
        [Site "Vienna"]
        [Date "2025.03.??"]
        [Round "3"]
        [White "Mustermann, Max"]
        [Black "Huber, Thomas"]
        [Result "1-0"]
        [WhiteElo "1850"]
        [BlackElo "0"]
        [TimeControl "90+30"]

        {A good game.} 1. e4 e5 2. Sf3 $1 Sc6 (2... d6 {Philidor} 3. d4 (3. Lc4)) 3. Lb5 a6!? 4. La4 Sf6 5. O-O Le7 1-0

        [Event "Vienna Open"]
        [Site "Vienna"]
        [Date "2025.04.12"]
        [Round "1"]
        [White "Berger, Anna"]
        [Black "Mustermann M."]
        [Result "0-1"]

        1. d4 d5 2. c4 e6 3. Nc3 Nf6 0-1

        [Event "Blitz"]
        [Site "?"]
        [Date "2025.??.??"]
        [Round "-"]
        [White "Maxi"]
        [Black "Schuster, Paul"]
        [Result "1/2-1/2"]
        [WhiteElo "1790"]
        [BlackElo "1702"]

        1. c4 c5 1/2-1/2

        [Event "Club Championship"]
        [White "Mustermann, Max"]
        [Black "Kaputt, Karl"]
        [Result "1-0"]

        1. e4 e5 2. Ke3 Nc6 1-0

        [Event "Some Event"]
        [White "Fischer, Robert"]
        [Black "Spassky, Boris"]
        [Result "1-0"]

        1. e4 e5 1-0

        [Event "Club Championship"]
        [White "Huber, Thomas"]
        [Black "Mustermann, Max"]
        [Result "*"]

        1. e4 c5 *

        [Event "Club Championship"]
        [Date "2025.05.01"]
        [White "Huber, Thomas"]
        [Black "Mustermann, Max"]
        [Result "0-1"]
        [WhiteElo "1880"]

        1. e4 c5 2. Nf3 d6 0-1
        """;

    private static async Task<HttpClient> CollectionWithOwnerAsync(CollectionFactory factory)
    {
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/api/settings", new { ownerName = "Max Mustermann", aliases = new[] { "Maxi" } }))
            .EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Importing_the_fixture_reports_what_happened_to_every_game()
    {
        using var factory = new CollectionFactory();
        var client = await CollectionWithOwnerAsync(factory);

        var summary = await client.ImportTextAsync(FixturePgn);

        Assert.Equal((4, 2, 1), (summary.Imported, summary.NeedsReview, summary.Failed));
        Assert.Equal(
            ["imported", "imported", "imported", "failed", "needsReview", "needsReview", "imported"],
            summary.Games.Select(g => g.Status));
        Assert.Contains("Ke3", summary.Games[3].Reason);
        Assert.Contains("match", summary.Games[4].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("result", summary.Games[5].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, (await client.ListGamesAsync()).Count);
    }

    [Fact]
    public async Task Variations_comments_symbols_and_german_letters_survive_import()
    {
        using var factory = new CollectionFactory();
        var client = await CollectionWithOwnerAsync(factory);

        var summary = await client.ImportTextAsync(FixturePgn);

        var game = await client.GetGameAsync(summary.Games[0].GameId!.Value);
        var main = game.MainLine();
        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4", "Nf6", "O-O", "Be7"], main.Select(n => n.San));
        Assert.Equal("A good game.", main[0].CommentBefore);
        Assert.Equal([1], main[2].Nags);
        Assert.Equal([5], main[5].Nags);

        var d6 = game.Node(game.Node(main[2].Id).Children[1]);
        Assert.Equal("d6", d6.San);
        Assert.Equal("Philidor", d6.CommentAfter);
        var d4 = game.Node(d6.Children[0]);
        Assert.Equal(["d4", "Bc4"], d6.Children.Select(id => game.Node(id).San));
        Assert.Equal("d4", d4.San);
    }

    [Fact]
    public async Task Game_details_and_elos_map_from_the_tags()
    {
        using var factory = new CollectionFactory();
        var client = await CollectionWithOwnerAsync(factory);

        var summary = await client.ImportTextAsync(FixturePgn);

        var first = await client.GetGameAsync(summary.Games[0].GameId!.Value);
        Assert.Equal(("white", "Huber, Thomas", "Max Mustermann"), (first.OwnerColour, first.Opponent.Name, first.White));
        Assert.Equal((1850, (int?)null), (first.WhiteElo, first.BlackElo));
        Assert.Equal(("Club Championship", "Vienna", "2025-03", "3", "90+30"),
            (first.Event!.Name, first.Site, first.Date, first.Round, first.TimeControl));
        Assert.Equal("1-0", first.Result);
        Assert.Equal(("C84", "Ruy Lopez: Closed"), (first.Eco, first.OpeningName));

        var second = await client.GetGameAsync(summary.Games[1].GameId!.Value);
        Assert.Equal(("black", "Berger, Anna"), (second.OwnerColour, second.Opponent.Name));
        Assert.Equal(((int?)null, (int?)null), (second.WhiteElo, second.BlackElo));
        Assert.Equal("2025-04-12", second.Date);

        var third = await client.GetGameAsync(summary.Games[2].GameId!.Value);
        Assert.Equal("white", third.OwnerColour);
        Assert.Equal(("2025", null, null), (third.Date, third.Site, third.Round));
    }

    [Fact]
    public async Task An_opponent_with_exactly_the_same_name_is_the_same_player()
    {
        using var factory = new CollectionFactory();
        var client = await CollectionWithOwnerAsync(factory);
        var existing = await client.CreatePlayerAsync("Huber, Thomas");

        var summary = await client.ImportTextAsync(FixturePgn);

        var first = await client.GetGameAsync(summary.Games[0].GameId!.Value);
        var last = await client.GetGameAsync(summary.Games[6].GameId!.Value);
        Assert.Equal(existing.Id, first.Opponent.Id);
        Assert.Equal(existing.Id, last.Opponent.Id);
        Assert.Equal(1880, last.WhiteElo);
    }

    [Fact]
    public async Task A_pgn_file_imports_like_pasted_text()
    {
        using var factory = new CollectionFactory();
        var client = await CollectionWithOwnerAsync(factory);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(FixturePgn)), "file", "games.pgn");

        var response = await client.PostAsync("/api/import/file", content);

        response.EnsureSuccessStatusCode();
        var summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        Assert.Equal((4, 2, 1), (summary.Imported, summary.NeedsReview, summary.Failed));
    }

    [Fact]
    public async Task A_latin1_encoded_file_keeps_its_umlauts()
    {
        using var factory = new CollectionFactory();
        var client = await CollectionWithOwnerAsync(factory);
        const string pgn = "[White \"Mustermann, Max\"]\n[Black \"Müller, Jörg\"]\n[Result \"1-0\"]\n\n1. e4 1-0\n";
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.Latin1.GetBytes(pgn)), "file", "old.pgn");

        var summary = (await (await client.PostAsync("/api/import/file", content)).Content.ReadFromJsonAsync<ImportSummaryDto>())!;

        Assert.Equal("Müller, Jörg", (await client.GetGameAsync(summary.Games[0].GameId!.Value)).Opponent.Name);
    }

    [Theory]
    [InlineData("Mustermann, Max")]
    [InlineData("Mustermann M.")]
    [InlineData("mustermann, max")]
    [InlineData("Max Mustermann")]
    [InlineData("Mustermann,M")]
    public async Task The_owner_is_recognised_in_the_usual_name_forms(string white)
    {
        using var factory = new CollectionFactory();
        var client = await CollectionWithOwnerAsync(factory);

        var summary = await client.ImportTextAsync($"[White \"{white}\"]\n[Black \"Huber, Thomas\"]\n[Result \"1-0\"]\n\n1. e4 1-0\n");

        Assert.Equal("imported", Assert.Single(summary.Games).Status);
    }

    [Theory]
    [InlineData("Mustermann, Moritz")]
    [InlineData("Mustermann, Anna")]
    [InlineData("Mustermeier, Max")]
    public async Task Similar_but_different_names_are_not_the_owner(string white)
    {
        using var factory = new CollectionFactory();
        var client = await CollectionWithOwnerAsync(factory);

        var summary = await client.ImportTextAsync($"[White \"{white}\"]\n[Black \"Huber, Thomas\"]\n[Result \"1-0\"]\n\n1. e4 1-0\n");

        Assert.Equal("needsReview", Assert.Single(summary.Games).Status);
    }

    [Fact]
    public async Task Aliases_are_managed_in_settings()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        await client.PutAsJsonAsync("/api/settings", new { ownerName = "Max Mustermann", aliases = new[] { " Maxi ", "M. M.", "" } });

        var settings = await client.GetFromJsonAsync<SettingsWithAliasesDto>("/api/settings");
        Assert.Equal(["Maxi", "M. M."], settings!.Aliases);
    }

    [Fact]
    public async Task Text_that_is_not_pgn_fails_without_importing_anything()
    {
        using var factory = new CollectionFactory();
        var client = await CollectionWithOwnerAsync(factory);

        var summary = await client.ImportTextAsync("[White \"Mustermann, Max\"]\n[Black \"X\"]\n[Result \"1-0\"]\n\n1. e4 {unterminated 1-0\n");

        Assert.Equal((0, 0, 1), (summary.Imported, summary.NeedsReview, summary.Failed));
        Assert.Empty(await client.ListGamesAsync());
    }

    [Fact]
    public async Task Games_from_a_custom_starting_position_fail_with_a_reason()
    {
        using var factory = new CollectionFactory();
        var client = await CollectionWithOwnerAsync(factory);

        var summary = await client.ImportTextAsync(
            "[White \"Mustermann, Max\"]\n[Black \"X\"]\n[Result \"1-0\"]\n[SetUp \"1\"]\n[FEN \"4k3/8/8/8/8/8/8/R3K3 w - - 0 1\"]\n\n1. Ra8# 1-0\n");

        var game = Assert.Single(summary.Games);
        Assert.Equal("failed", game.Status);
        Assert.Contains("starting position", game.Reason);
    }

    [Fact]
    public async Task Empty_input_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/import/text", new { pgn = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public sealed record ImportSummaryDto(int Imported, int NeedsReview, int Failed, List<ImportedGameDto> Games);

public sealed record ImportedGameDto(int Index, string Status, string? Reason, string? White, string? Black, int? GameId);

public sealed record SettingsWithAliasesDto(string OwnerName, List<string> Aliases);
