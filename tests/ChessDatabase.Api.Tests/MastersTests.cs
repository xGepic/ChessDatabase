using System.Net;
using System.Net.Http.Json;
using System.Text;
using ChessDatabase.Api.Research;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ChessDatabase.Api.Tests;

/// <summary>
/// The Masters source of the Opening Tree, with Lichess replaced by a fake HTTP handler.
/// </summary>
public class MastersTests
{
    private const string StartJson = """
        {"white":958331,"draws":1346565,"black":692516,
         "moves":[
           {"uci":"e2e4","san":"e4","averageRating":2390,"white":431551,"draws":610338,"black":326236,"game":null},
           {"uci":"d2d4","san":"d4","averageRating":2405,"white":342556,"draws":488326,"black":239633,"game":null}],
         "topGames":[
           {"uci":"e2e4","id":"dfYKaW8J","winner":null,"white":{"name":"Carlsen, M.","rating":2839},
            "black":{"name":"Nakamura, Hi","rating":2816},"year":2025,"month":"2025-10"},
           {"uci":"d2d4","id":"aAbqI4ey","winner":"white","white":{"name":"Kasparov, G.","rating":2851},
            "black":{"name":"Karpov, A.","rating":2730},"year":1990,"month":"1990-10"}],
         "opening":null}
        """;

    private const string GamePgn = """
        [Event "Clutch Chess: Champions"]
        [Site "Saint Louis USA"]
        [Date "2025.10.29"]
        [White "Carlsen, M."]
        [Black "Nakamura, Hi"]
        [Result "1/2-1/2"]
        [WhiteElo "2839"]
        [BlackElo "2816"]

        1. e4 c5 2. Nf3 g6 3. d4 cxd4 1/2-1/2
        """;

    [Fact]
    public async Task The_Masters_tab_shows_moves_with_game_counts_and_result_percentages()
    {
        var lichess = new FakeLichess().Json("/masters", StartJson);
        using var factory = MastersFactory(lichess);
        var client = factory.CreateClient();

        var masters = await client.MastersAsync(ResearchTestExtensions.Fen());

        Assert.True(masters.Available);
        Assert.Equal(2_997_412, masters.GameCount);
        var e4 = masters.Moves[0];
        Assert.Equal(("e2e4", "e4", 1_368_125, 2390), (e4.Uci, e4.San, e4.GameCount, e4.AverageElo));
        Assert.Equal(new ResultShareDto(31.5, 44.6, 23.8), e4.Results);
        Assert.Equal(ResearchTestExtensions.Fen("e4"), e4.Fen);
        Assert.Equal(["e4", "d4"], masters.Moves.Select(m => m.San));
        var request = Assert.Single(lichess.Requests);
        Assert.Contains("fen=rnbqkbnr%2Fpppppppp%2F8%2F8%2F8%2F8%2FPPPPPPPP%2FRNBQKBNR%20w%20KQkq%20-&", request.RequestUri!.Query);
        Assert.Equal(2_997_412 - 1_368_125 - 1_070_515, masters.OtherMovesGameCount); // moves beyond the ones listed
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task The_top_Master_Games_are_listed()
    {
        using var factory = MastersFactory(new FakeLichess().Json("/masters", StartJson));
        var client = factory.CreateClient();

        var masters = await client.MastersAsync(ResearchTestExtensions.Fen());

        Assert.Equal(
            [("dfYKaW8J", "Carlsen, M.", 2839, "Nakamura, Hi", 2816, 2025, "1/2-1/2"), ("aAbqI4ey", "Kasparov, G.", 2851, "Karpov, A.", 2730, 1990, "1-0")],
            masters.TopGames.Select(g => (g.Id, g.White.Name, g.White.Elo, g.Black.Name, g.Black.Elo, g.Year, g.Result)));
    }

    [Fact]
    public async Task A_Master_Game_can_be_viewed_and_is_never_added_to_the_Collection()
    {
        var lichess = new FakeLichess().Text("/masters/pgn/dfYKaW8J", GamePgn);
        using var factory = MastersFactory(lichess);
        var client = factory.CreateClient();

        var game = (await client.GetFromJsonAsync<MasterGameDto>(
            $"/api/research/masters/games/dfYKaW8J?fen={Uri.EscapeDataString(ResearchTestExtensions.Fen("e4", "c5"))}"))!;

        Assert.Equal(("Carlsen, M.", 2839, "Nakamura, Hi", 2816, "1/2-1/2"), (game.White.Name, game.White.Elo, game.Black.Name, game.Black.Elo, game.Result));
        Assert.Equal(("Clutch Chess: Champions", "Saint Louis USA", "2025-10-29"), (game.Event, game.Site, game.Date));
        Assert.Equal(["e4", "c5", "Nf3", "g6", "d4", "cxd4"], game.Moves.Select(m => m.San));
        Assert.Equal(ResearchTestExtensions.Fen("e4", "c5", "Nf3", "g6", "d4", "cxd4"), game.Moves[^1].Fen);
        Assert.Equal(2, game.Ply); // where the board position occurs in it
        Assert.Empty(await client.ListGamesAsync());
    }

    [Fact]
    public async Task Results_are_kept_for_the_session_so_Lichess_is_asked_once()
    {
        var lichess = new FakeLichess().Json("/masters", StartJson).Text("/masters/pgn/dfYKaW8J", GamePgn);
        using var factory = MastersFactory(lichess);
        var client = factory.CreateClient();

        await client.MastersAsync(ResearchTestExtensions.Fen());
        await client.MastersAsync(ResearchTestExtensions.Fen());
        await client.GetAsync("/api/research/masters/games/dfYKaW8J");
        await client.GetAsync("/api/research/masters/games/dfYKaW8J");

        Assert.Equal(2, lichess.Requests.Count);
    }

    [Fact]
    public async Task The_same_position_at_another_move_number_comes_from_the_cache()
    {
        var lichess = new FakeLichess().Json("/masters", StartJson);
        using var factory = MastersFactory(lichess);
        var client = factory.CreateClient();
        // 1.Nf3 Nf6 2.Ng1 Ng8 brings back the starting position, at move 3 with a halfmove clock of 4.
        var again = ResearchTestExtensions.Fen("Nf3", "Nf6", "Ng1", "Ng8");

        await client.MastersAsync(ResearchTestExtensions.Fen());
        var cached = await client.MastersAsync(again);

        Assert.True(cached.Available);
        Assert.Single(lichess.Requests);
    }

    [Fact]
    public async Task An_empty_answer_is_unavailable_and_not_kept()
    {
        var lichess = new FakeLichess().Json("/masters", "null");
        using var factory = MastersFactory(lichess);
        var client = factory.CreateClient();

        var first = await client.MastersAsync(ResearchTestExtensions.Fen());
        await client.MastersAsync(ResearchTestExtensions.Fen());

        Assert.False(first.Available);
        Assert.Contains("unexpected", first.Message);
        Assert.Equal(2, lichess.Requests.Count);
    }

    [Fact]
    public async Task Waiting_behind_a_slow_request_is_not_blamed_on_Lichess()
    {
        var lichess = new FakeLichess { Delay = TimeSpan.FromSeconds(3) }.Json("/masters", StartJson);
        using var factory = MastersFactory(lichess, timeoutSeconds: "10");
        factory.Settings["Masters:QueueSeconds"] = "1";
        var client = factory.CreateClient();

        var slow = client.MastersAsync(ResearchTestExtensions.Fen());
        await Task.Delay(300);
        var queued = await client.MastersAsync(ResearchTestExtensions.Fen("e4"));

        Assert.False(queued.Available);
        Assert.Contains("busy with earlier requests", queued.Message);
        Assert.True((await slow).Available);
    }

    [Fact]
    public async Task A_slow_Lichess_shows_Masters_unavailable_within_a_short_timeout()
    {
        var lichess = new FakeLichess { Delay = TimeSpan.FromSeconds(30) }.Json("/masters", StartJson);
        using var factory = MastersFactory(lichess, timeoutSeconds: "1");
        var client = factory.CreateClient();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var masters = await client.MastersAsync(ResearchTestExtensions.Fen());

        Assert.False(masters.Available);
        Assert.Contains("Masters unavailable", masters.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "Masters unavailable")]
    [InlineData(HttpStatusCode.BadGateway, "Masters unavailable")]
    [InlineData(HttpStatusCode.Unauthorized, "Lichess API token")]
    public async Task An_error_from_Lichess_shows_why_and_nothing_else_breaks(HttpStatusCode status, string message)
    {
        using var factory = MastersFactory(new FakeLichess().Status("/masters", status));
        var client = factory.CreateClient();

        var masters = await client.MastersAsync(ResearchTestExtensions.Fen());

        Assert.False(masters.Available);
        Assert.Contains(message, masters.Message);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/research/tree?fen={Uri.EscapeDataString(ResearchTestExtensions.Fen())}")).StatusCode);
    }

    [Fact]
    public async Task Offline_shows_Masters_unavailable()
    {
        using var factory = MastersFactory(new FakeLichess { Offline = true });
        var client = factory.CreateClient();

        var masters = await client.MastersAsync(ResearchTestExtensions.Fen());

        Assert.False(masters.Available);
        Assert.Contains("Masters unavailable", masters.Message);
    }

    [Fact]
    public async Task After_too_many_requests_Lichess_is_left_alone_for_a_minute()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lichess = new FakeLichess().Status("/masters", (HttpStatusCode)429);
        using var factory = MastersFactory(lichess, clock: clock);
        var client = factory.CreateClient();

        Assert.False((await client.MastersAsync(ResearchTestExtensions.Fen())).Available);
        Assert.False((await client.MastersAsync(ResearchTestExtensions.Fen("e4"))).Available);
        Assert.Single(lichess.Requests);

        clock.Advance(TimeSpan.FromSeconds(61));
        await client.MastersAsync(ResearchTestExtensions.Fen("e4"));
        Assert.Equal(2, lichess.Requests.Count);
    }

    [Fact]
    public async Task A_Lichess_token_is_sent_but_never_shown_and_never_stored_in_the_Collection()
    {
        var lichess = new FakeLichess().Json("/masters", StartJson);
        using var factory = MastersFactory(lichess);
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");

        (await client.PutAsJsonAsync("/api/settings/lichess-token", new { token = " lip_secret " })).EnsureSuccessStatusCode();
        await client.MastersAsync(ResearchTestExtensions.Fen());

        Assert.Equal("Bearer lip_secret", Assert.Single(lichess.Requests).Headers.Authorization?.ToString());
        var settings = await client.GetStringAsync("/api/settings");
        Assert.DoesNotContain("lip_secret", settings);
        Assert.Contains("\"hasLichessToken\":true", settings);
        Assert.Contains("Max Mustermann", settings); // saving the token left the other settings alone

        // Kept outside the Collection, encrypted: neither the database nor a backup holds it.
        var backup = await client.BackUpNowAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(factory.Directory, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain("lip_secret", Encoding.UTF8.GetString(await ReadSharedAsync(file)));
        Assert.Single(Directory.GetFiles(backup.Folder, "*.db"));

        (await client.PutAsJsonAsync("/api/settings/lichess-token", new { token = "" })).EnsureSuccessStatusCode();
        Assert.Contains("\"hasLichessToken\":false", await client.GetStringAsync("/api/settings"));
    }

    private static async Task<byte[]> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    [Fact]
    public async Task An_impossible_position_or_game_id_is_refused_without_asking_Lichess()
    {
        var lichess = new FakeLichess();
        using var factory = MastersFactory(lichess);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/research/masters?fen=nonsense")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/research/masters/games/..%2Fapi")).StatusCode);
        Assert.Empty(lichess.Requests);
    }

    private static CollectionFactory MastersFactory(FakeLichess lichess, string timeoutSeconds = "5", TimeProvider? clock = null)
    {
        var factory = new CollectionFactory(clock: clock)
        {
            ConfigureServices = services => services.AddHttpClient<MastersClient>().ConfigurePrimaryHttpMessageHandler(() => lichess),
        };
        factory.Settings["Masters:TimeoutSeconds"] = timeoutSeconds;
        return factory;
    }
}

/// <summary>Lichess's explorer, faked: canned answers by path, and every request it received.</summary>
public sealed class FakeLichess : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _answers = [];
    private readonly List<HttpRequestMessage> _requests = [];

    public TimeSpan Delay { get; init; }
    public bool Offline { get; init; }

    public IReadOnlyList<HttpRequestMessage> Requests { get { lock (_requests) return [.. _requests]; } }

    public FakeLichess Json(string path, string json) =>
        Answer(path, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public FakeLichess Text(string path, string text) =>
        Answer(path, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/x-chess-pgn") });

    public FakeLichess Status(string path, HttpStatusCode status) => Answer(path, () => new HttpResponseMessage(status));

    private FakeLichess Answer(string path, Func<HttpResponseMessage> answer)
    {
        _answers[path] = answer;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requests) _requests.Add(request);
        if (Offline) throw new HttpRequestException("No such host is known.");
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
        return _answers.TryGetValue(request.RequestUri!.AbsolutePath, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

public sealed record MasterPlayerDto(string Name, int? Elo);

public sealed record ResultShareDto(double White, double Draw, double Black);

public sealed record MastersMoveDto(string Uci, string San, string Fen, long GameCount, ResultShareDto Results, int? AverageElo);

public sealed record MasterGameRefDto(string Id, MasterPlayerDto White, MasterPlayerDto Black, int? Year, string Result);

public sealed record MastersDto(
    bool Available, string? Message, long GameCount, List<MastersMoveDto> Moves, long OtherMovesGameCount, List<MasterGameRefDto> TopGames);

public sealed record MasterMoveDto(string San, string Uci, string Fen);

public sealed record MasterGameDto(
    string Id, MasterPlayerDto White, MasterPlayerDto Black, string Result, string? Event, string? Site, string? Date,
    List<MasterMoveDto> Moves, int? Ply);

public static class MastersTestExtensions
{
    public static async Task<MastersDto> MastersAsync(this HttpClient client, string fen) =>
        (await client.GetFromJsonAsync<MastersDto>($"/api/research/masters?fen={Uri.EscapeDataString(fen)}"))!;
}
