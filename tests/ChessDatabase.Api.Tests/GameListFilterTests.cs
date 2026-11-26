using System.Net;
using System.Net.Http.Json;

namespace ChessDatabase.Api.Tests;

public class GameListFilterTests
{
    /// <summary>A small Collection with known answers to every filter.</summary>
    private const string Seed = """
        [Event "Club League"] [Date "2025.02.10"] [White "Mustermann, Max"] [Black "Huber, Thomas"] [Result "0-1"]
        [WhiteElo "1850"] [BlackElo "1900"] [TimeControl "90+30"] [Tags "league"]
        1. e4 c5 0-1

        [Event "Club League"] [Date "2025.03.12"] [White "Berger, Anna"] [Black "Mustermann, Max"] [Result "1-0"]
        [WhiteElo "1700"] [TimeControl "90+30"] [Tags "league; must review"]
        1. d4 d5 2. c4 1-0

        [Event "Club League"] [Date "2024.11.05"] [White "Huber, Thomas"] [Black "Mustermann, Max"] [Result "1-0"]
        [WhiteElo "1880"] [BlackElo "1840"] [TimeControl "90+30"]
        1. e4 e5 1-0

        [Event "Vienna Open"] [Date "2025.07"] [White "Schuster, Paul"] [Black "Mustermann, Max"] [Result "1/2-1/2"]
        [TimeControl "120+30"]
        1. c4 e5 1/2-1/2

        [Event "Blitz Night"] [Date "2025"] [White "Mustermann, Max"] [Black "Berger, Anna"] [Result "1-0"]
        [TimeControl "3+2"] [Tags "blitz"]
        1. e4 e6 1-0
        """;

    private static async Task<HttpClient> SeededAsync(CollectionFactory factory)
    {
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var summary = await client.ImportTextAsync(Seed.Replace("] [", "]\n["));
        Assert.Equal(5, summary.Imported);
        return client;
    }

    private static async Task<List<string>> OpponentsAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<List<GameSummaryDto>>($"/api/games?{query}"))!
            .Select(g => $"{g.Opponent.Name} {g.Date}").ToList();

    [Fact]
    public async Task Combined_filters_answer_my_losses_with_black_in_the_club_league_in_2025()
    {
        using var factory = new CollectionFactory();
        var client = await SeededAsync(factory);
        var league = (await client.GetFromJsonAsync<List<NamedDto>>("/api/events?search=Club League"))!.Single();

        var games = await OpponentsAsync(client, $"result=loss&ownerColour=black&eventId={league.Id}&from=2025&to=2025");

        Assert.Equal(["Berger, Anna 2025-03-12"], games);
    }

    [Theory]
    [InlineData("result=win", "Berger, Anna 2025")]
    [InlineData("result=draw", "Schuster, Paul 2025-07")]
    [InlineData("result=loss", "Berger, Anna 2025-03-12|Huber, Thomas 2025-02-10|Huber, Thomas 2024-11-05")]
    [InlineData("ownerColour=white", "Huber, Thomas 2025-02-10|Berger, Anna 2025")]
    [InlineData("from=2025-03&to=2025-07-15", "Schuster, Paul 2025-07|Berger, Anna 2025-03-12|Berger, Anna 2025")]
    [InlineData("from=2025-03", "Schuster, Paul 2025-07|Berger, Anna 2025-03-12|Berger, Anna 2025")]
    [InlineData("to=2024-12-31", "Huber, Thomas 2024-11-05")]
    [InlineData("ecoFrom=A00&ecoTo=A99", "Schuster, Paul 2025-07")]
    [InlineData("ecoFrom=C00", "Berger, Anna 2025-03-12|Berger, Anna 2025|Huber, Thomas 2024-11-05")]
    [InlineData("tag=LEAGUE", "Berger, Anna 2025-03-12|Huber, Thomas 2025-02-10")]
    [InlineData("timeControl=90%2B30", "Berger, Anna 2025-03-12|Huber, Thomas 2025-02-10|Huber, Thomas 2024-11-05")]
    public async Task Each_filter_works_on_its_own(string query, string expected)
    {
        using var factory = new CollectionFactory();
        var client = await SeededAsync(factory);

        var games = await OpponentsAsync(client, query);

        Assert.Equal(expected.Split('|'), games);
    }

    [Fact]
    public async Task Filtering_by_opponent_finds_all_games_against_that_player()
    {
        using var factory = new CollectionFactory();
        var client = await SeededAsync(factory);
        var huber = (await client.GetFromJsonAsync<List<PlayerDto>>("/api/players?search=Huber"))!.Single();

        var games = await OpponentsAsync(client, $"opponentId={huber.Id}");

        Assert.Equal(["Huber, Thomas 2025-02-10", "Huber, Thomas 2024-11-05"], games);
    }

    [Theory]
    [InlineData("date", "asc", "2024-11-05|2025|2025-02-10|2025-03-12|2025-07")]
    [InlineData("date", "desc", "2025-07|2025-03-12|2025-02-10|2025|2024-11-05")]
    [InlineData("whiteElo", "desc", "1880|1850|1700|-|-")]
    [InlineData("whiteElo", "asc", "1700|1850|1880|-|-")]
    [InlineData("opponent", "asc", "Berger, Anna|Berger, Anna|Huber, Thomas|Huber, Thomas|Schuster, Paul")]
    [InlineData("event", "desc", "Vienna Open|Club League|Club League|Club League|Blitz Night")]
    [InlineData("eco", "asc", "A20|B20|C00|C20|D06")]
    public async Task Every_column_sorts_both_ways_with_empty_values_last(string sort, string direction, string expected)
    {
        using var factory = new CollectionFactory();
        var client = await SeededAsync(factory);

        var games = (await client.GetFromJsonAsync<List<GameSummaryDto>>($"/api/games?sort={sort}&direction={direction}"))!;

        string Value(GameSummaryDto g) => sort switch
        {
            "date" => g.Date ?? "-",
            "whiteElo" => g.WhiteElo?.ToString() ?? "-",
            "opponent" => g.Opponent.Name,
            "event" => g.Event?.Name ?? "-",
            _ => g.Eco ?? "-",
        };
        Assert.Equal(expected.Split('|'), games.Select(Value));
    }

    [Theory]
    [InlineData("white")]
    [InlineData("black")]
    [InlineData("blackElo")]
    [InlineData("result")]
    [InlineData("ownerColour")]
    public async Task The_remaining_columns_sort_too(string sort)
    {
        using var factory = new CollectionFactory();
        var client = await SeededAsync(factory);

        var asc = (await client.GetFromJsonAsync<List<GameSummaryDto>>($"/api/games?sort={sort}&direction=asc"))!;
        var desc = (await client.GetFromJsonAsync<List<GameSummaryDto>>($"/api/games?sort={sort}&direction=desc"))!;

        Assert.Equal(5, asc.Count);
        Assert.NotEqual(asc.Select(g => g.Id), desc.Select(g => g.Id));
    }

    [Theory]
    [InlineData("result=maybe")]
    [InlineData("ownerColour=green")]
    [InlineData("from=March")]
    [InlineData("sort=height")]
    [InlineData("direction=up")]
    public async Task Unknown_filter_values_are_refused(string query)
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/games?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_filtered_list_exports_exactly_the_games_shown()
    {
        using var factory = new CollectionFactory();
        var client = await SeededAsync(factory);
        const string query = "ownerColour=black&result=loss";

        var shown = (await client.GetFromJsonAsync<List<GameSummaryDto>>($"/api/games?{query}"))!;
        var pgn = await client.GetStringAsync($"/api/games/pgn?{query}");

        var exportedBlackPlayers = pgn.Split('\n').Where(l => l.StartsWith("[White ")).ToList();
        Assert.Equal(shown.Count, exportedBlackPlayers.Count);
        Assert.Equal(shown.Select(g => $"[White \"{g.White}\"]"), exportedBlackPlayers);
    }
}
