using System.Net;
using System.Net.Http.Json;
using ChessDatabase.Api.Research;
using Microsoft.Extensions.DependencyInjection;

namespace ChessDatabase.Api.Tests;

/// <summary>Tablebase lookups, with Lichess's tablebase replaced by a fake HTTP handler.</summary>
public class TablebaseTests
{
    /// <summary>K+P against K+P, White to move. The verdicts below are canned, in Lichess's shape.</summary>
    private const string Fen = "4k3/6KP/8/8/8/8/7p/8 w - - 0 1";

    // Lichess rates each move from the side to move after it (the opponent): "loss" there is a win for the mover.
    private const string Answer = """
        {"checkmate":false,"stalemate":false,"insufficient_material":false,"dtz":1,"precise_dtz":1,"dtm":17,"category":"win",
         "moves":[
           {"uci":"h7h8q","san":"h8=Q+","zeroing":true,"checkmate":false,"stalemate":false,"dtz":-2,"precise_dtz":-2,"dtm":-16,"category":"loss"},
           {"uci":"g7f6","san":"Kf6","zeroing":false,"checkmate":false,"stalemate":false,"dtz":1,"precise_dtz":1,"dtm":19,"category":"win"},
           {"uci":"g7g8","san":"Kg8","zeroing":false,"checkmate":false,"stalemate":false,"dtz":0,"precise_dtz":0,"dtm":0,"category":"draw"}]}
        """;

    [Fact]
    public async Task An_endgame_shows_the_exact_verdict_and_the_moves_ranked_best_first()
    {
        var lichess = new FakeLichess().Json("/standard", Answer);
        using var factory = TablebaseFactory(lichess);
        var client = factory.CreateClient();

        var tablebase = await client.TablebaseAsync(Fen);

        Assert.True(tablebase.Applicable);
        Assert.True(tablebase.Available);
        Assert.Equal(("win", "1-0", 1, 17), (tablebase.Category, tablebase.Outcome, tablebase.Dtz, tablebase.Dtm));
        Assert.Equal(
            [("h8=Q+", "win", 2, 16, true), ("Kg8", "draw", 0, 0, false), ("Kf6", "loss", -1, -19, false)],
            tablebase.Moves.Select(m => (m.San, m.Category, m.Dtz, m.Dtm, m.Zeroing)));
        Assert.Equal(Position("4k2Q/6K1/8/8/8/8/7p/8 b - - 0 1"), Position(tablebase.Moves[0].Fen));
        Assert.Contains("fen=4k3%2F6KP%2F8%2F8%2F8%2F8%2F7p%2F8%20w%20-%20-", Assert.Single(lichess.Requests).RequestUri!.Query);
    }

    [Fact]
    public async Task Every_Lichess_verdict_is_understood_including_syzygy_and_the_50_move_rule()
    {
        // syzygy-win/-loss: decided by Syzygy alone (no DTM); cursed and blessed: the 50-move rule turns them into draws.
        var lichess = new FakeLichess().Json("/standard", """
            {"dtz":3,"dtm":null,"category":"syzygy-win",
             "moves":[
               {"uci":"g7g8","zeroing":false,"dtz":0,"dtm":null,"category":"draw"},
               {"uci":"g7f6","zeroing":false,"dtz":120,"dtm":null,"category":"blessed-loss"},
               {"uci":"h7h8q","zeroing":true,"dtz":-2,"dtm":null,"category":"syzygy-loss"},
               {"uci":"g7g6","zeroing":false,"dtz":-110,"dtm":null,"category":"cursed-win"},
               {"uci":"g7h6","zeroing":false,"dtz":null,"dtm":null,"category":"something-new"}]}
            """);
        using var factory = TablebaseFactory(lichess);
        var client = factory.CreateClient();

        var tablebase = await client.TablebaseAsync(Fen);

        Assert.Equal(("win", "1-0"), (tablebase.Category, tablebase.Outcome));
        Assert.Equal(
            [("h8=Q+", "win"), ("Kf6", "cursedWin"), ("Kg8", "draw"), ("Kh6", "unknown"), ("Kg6", "blessedLoss")],
            tablebase.Moves.Select(m => (m.San, m.Category)));
    }

    [Fact]
    public async Task A_position_with_more_than_seven_pieces_shows_nothing_and_asks_nobody()
    {
        var lichess = new FakeLichess().Json("/standard", Answer);
        using var factory = TablebaseFactory(lichess);
        var client = factory.CreateClient();

        var tablebase = await client.TablebaseAsync("4k3/pppp4/8/8/8/8/PPPP4/4K3 w - - 0 1");

        Assert.False(tablebase.Applicable);
        Assert.Empty(lichess.Requests);
    }

    [Fact]
    public async Task Answers_are_kept_for_the_session()
    {
        var lichess = new FakeLichess().Json("/standard", Answer);
        using var factory = TablebaseFactory(lichess);
        var client = factory.CreateClient();

        await client.TablebaseAsync(Fen);
        await client.TablebaseAsync(Fen.Replace(" 0 1", " 12 40"));

        Assert.Single(lichess.Requests);
    }

    [Fact]
    public async Task A_slow_tablebase_is_unavailable_within_a_short_timeout()
    {
        var lichess = new FakeLichess { Delay = TimeSpan.FromSeconds(30) }.Json("/standard", Answer);
        using var factory = TablebaseFactory(lichess, timeoutSeconds: "1");
        var client = factory.CreateClient();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var tablebase = await client.TablebaseAsync(Fen);

        Assert.False(tablebase.Available);
        Assert.Contains("Tablebase unavailable", tablebase.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    [Theory]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    [InlineData(false, HttpStatusCode.BadRequest)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task Errors_and_offline_are_unavailable_and_nothing_breaks(bool offline, HttpStatusCode status)
    {
        var lichess = new FakeLichess { Offline = offline }.Status("/standard", status);
        using var factory = TablebaseFactory(lichess);
        var client = factory.CreateClient();

        var tablebase = await client.TablebaseAsync(Fen);

        Assert.True(tablebase.Applicable);
        Assert.False(tablebase.Available);
        Assert.Contains("Tablebase unavailable", tablebase.Message);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/research/tree?fen={Uri.EscapeDataString(Fen)}")).StatusCode);
    }

    private static string Position(string fen) => string.Join(' ', fen.Split(' ').Take(4));

    private static CollectionFactory TablebaseFactory(FakeLichess lichess, string timeoutSeconds = "5")
    {
        var factory = new CollectionFactory
        {
            ConfigureServices = services => services.AddHttpClient<TablebaseClient>().ConfigurePrimaryHttpMessageHandler(() => lichess),
        };
        factory.Settings["Tablebase:TimeoutSeconds"] = timeoutSeconds;
        return factory;
    }
}

public sealed record TablebaseMoveDto(string Uci, string San, string Fen, string Category, int? Dtz, int? Dtm, bool Zeroing);

public sealed record TablebaseDto(
    bool Applicable, bool Available, string? Message, string? Category, string? Outcome, int? Dtz, int? Dtm, List<TablebaseMoveDto> Moves);

public static class TablebaseTestExtensions
{
    public static async Task<TablebaseDto> TablebaseAsync(this HttpClient client, string fen) =>
        (await client.GetFromJsonAsync<TablebaseDto>($"/api/research/tablebase?fen={Uri.EscapeDataString(fen)}"))!;
}
