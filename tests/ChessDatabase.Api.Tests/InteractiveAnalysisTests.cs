using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ChessDatabase.Api.Engines;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace ChessDatabase.Api.Tests;

public class EngineSettingsTests
{
    [Fact]
    public async Task Stockfish_starts_unset_with_the_default_options()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var engine = Assert.Single((await client.GetFromJsonAsync<List<EngineDefinitionDto>>("/api/engines"))!);

        Assert.Equivalent(new EngineDefinitionDto(1, "Stockfish", "", 16, 8192, 3, null, 2.0, null, []), engine, strict: true);
    }

    [Fact]
    public async Task Saving_a_path_checks_that_it_is_a_UCI_engine_and_records_its_name()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine { Name = "Fake Engine 2.5" };

        await client.UseAsync(fake, threads: 4, hashMb: 256, multiPv: 2);

        var engine = Assert.Single((await client.GetFromJsonAsync<List<EngineDefinitionDto>>("/api/engines"))!);
        Assert.Equal(FakeEngine.ExePath, engine.Path);
        Assert.Equal((4, 256, 2), (engine.Threads, engine.HashMb, engine.MultiPv));
        Assert.Equal("Fake Engine 2.5", engine.ReportedName);
    }

    [Fact]
    public async Task A_missing_file_is_refused_with_a_clear_message()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.PutEngineAsync(new { path = @"C:\nowhere\stockfish.exe", threads = 16, hashMb = 8192, multiPv = 3 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("There is no file at", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_program_that_is_not_a_UCI_engine_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var notAnEngine = Path.Combine(Environment.SystemDirectory, "hostname.exe");

        var response = await client.PutEngineAsync(new { path = notAnEngine, threads = 16, hashMb = 8192, multiPv = 3 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("did not answer as a UCI engine", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Options_out_of_range_are_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();

        var response = await client.PutEngineAsync(new { path = "", threads = 0, hashMb = 8192, multiPv = 4 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("threads", body);
        Assert.Contains("multiPv", body);
    }
}

public class InteractiveAnalysisTests
{
    [Fact]
    public async Task Turning_the_Engine_on_streams_up_to_three_lines_in_SAN_with_White_scores()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        // After 1.e4 it is Black to move: the engine's scores are Black's, the app shows White's.
        using var fake = new FakeEngine().Answer(["e2e4"], "e7e5",
            "depth 1 multipv 1 score cp -20 pv e7e5",
            "depth 1 multipv 2 score cp -30 pv c7c5",
            "depth 1 multipv 3 score cp -35 pv e7e6",
            "depth 2 seldepth 3 multipv 1 score cp -25 nodes 100 nps 1000 pv e7e5 g1f3",
            "depth 2 currmove e7e5 currmovenumber 1",
            "depth 2 multipv 2 score cp -31 pv c7c5 g1f3",
            "depth 2 multipv 3 score mate -4 pv e7e6 d2d4");
        await client.UseAsync(fake);
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseAsync(7, "e2e4");

        var update = await analysis.WaitForAsync(u => u.Lines.Count == 3 && u.Lines.All(l => l.Depth == 2));
        Assert.Equal(7, update.RequestId);
        var best = update.Lines[0];
        Assert.Equal((1, 2, 25, (int?)null), (best.Rank, best.Depth, best.Cp, best.Mate));
        Assert.Equal(["e5", "Nf3"], best.San);
        Assert.Equal(["e7e5", "g1f3"], best.Uci);
        Assert.Equal(["c5", "Nf3"], update.Lines[1].San);
        Assert.Equal((null, 4), (update.Lines[2].Cp, update.Lines[2].Mate));
        Assert.Equal(52.3, best.WhitePercent);
        Assert.Contains(analysis.Updates, u => u.Lines.All(l => l.Depth == 1)); // it streamed the earlier depth first
    }

    [Fact]
    public async Task The_configured_options_are_sent_to_the_Engine()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine();
        await client.UseAsync(fake, threads: 6, hashMb: 512, multiPv: 2);
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseAsync(1);
        await analysis.WaitForAsync(_ => true);

        var commands = fake.Commands();
        Assert.Contains("setoption name Threads value 6", commands);
        Assert.Contains("setoption name Hash value 512", commands);
        Assert.Contains("setoption name MultiPV value 2", commands);
        Assert.Contains("go infinite", commands);
    }

    [Fact]
    public async Task With_a_time_limit_the_Engine_searches_that_long_and_its_lines_stay()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine();
        await client.UseAsync(fake, interactiveSecondsLimit: 5);
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseAsync(1);
        await analysis.WaitForAsync(u => u.RequestId == 1);
        await analysis.AnalyseAsync(2, "e2e4");

        await analysis.WaitForAsync(u => u.RequestId == 2);
        Assert.Equal(2, fake.Commands().Count(c => c == "go movetime 5000"));
        Assert.DoesNotContain("go infinite", fake.Commands());
    }

    [Fact]
    public async Task Moving_to_another_position_restarts_the_analysis_there()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine()
            .Answer([], "e2e4", "depth 5 multipv 1 score cp 30 pv e2e4")
            .Answer(["e2e4"], "c7c5", "depth 6 multipv 1 score cp -28 pv c7c5");
        await client.UseAsync(fake);
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseAsync(1);
        await analysis.WaitForAsync(u => u.RequestId == 1);
        await analysis.AnalyseAsync(2, "e2e4");

        var update = await analysis.WaitForAsync(u => u.RequestId == 2);
        Assert.Equal(["c5"], update.Lines[0].San);
        var commands = fake.Commands();
        var stop = Array.IndexOf(commands, "stop");
        Assert.True(stop > 0 && stop < Array.LastIndexOf(commands, "position startpos moves e2e4"), string.Join(" | ", commands));
        Assert.Single(commands, c => c.StartsWith("setoption name Threads")); // the same Engine process follows along
    }

    [Fact]
    public async Task Turning_the_Engine_off_ends_its_process()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine();
        await client.UseAsync(fake);
        var host = factory.Services.GetRequiredService<EngineHost>();
        await using var analysis = await factory.ConnectAnalysisAsync();
        await analysis.AnalyseAsync(1);
        await analysis.WaitForAsync(_ => true);
        var process = Process.GetProcessById(Assert.Single(host.RunningProcessIds));

        await analysis.StopAsync();

        Assert.Empty(host.RunningProcessIds);
        Assert.True(process.WaitForExit(5000));
        Assert.Contains("quit", fake.Commands());
    }

    [Fact]
    public async Task Leaving_the_Game_or_closing_the_app_ends_the_process()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine();
        await client.UseAsync(fake);
        var host = factory.Services.GetRequiredService<EngineHost>();

        var first = await factory.ConnectAnalysisAsync();
        await first.AnalyseAsync(1);
        await first.WaitForAsync(_ => true);
        var leaving = Process.GetProcessById(Assert.Single(host.RunningProcessIds));
        await first.DisposeAsync(); // the connection closes when the Game view goes away
        Assert.True(leaving.WaitForExit(5000));
        await Wait.UntilAsync(() => host.RunningProcessIds.Count == 0);

        var second = await factory.ConnectAnalysisAsync();
        await second.AnalyseAsync(1);
        await second.WaitForAsync(_ => true);
        var running = Process.GetProcessById(Assert.Single(host.RunningProcessIds));
        await factory.DisposeAsync(); // the app closes
        Assert.True(running.WaitForExit(5000));
    }

    [Fact]
    public async Task Closing_the_Game_view_while_the_Engine_is_starting_leaves_no_process_behind()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine();
        await client.UseAsync(fake);
        var analysis = factory.Services.GetRequiredService<InteractiveAnalysis>();
        var host = factory.Services.GetRequiredService<EngineHost>();

        var starting = analysis.AnalyseAsync("connection-1", 1, []);
        var closing = analysis.CloseAsync("connection-1");
        await Task.WhenAll(starting, closing);

        await Wait.UntilAsync(() => host.RunningProcessIds.Count == 0, "no Engine may outlive its connection");
    }

    [Fact]
    public async Task Changing_the_running_engine_in_settings_restarts_it_with_the_new_settings()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var stockfish = new FakeEngine { Name = "Fake Stockfish" };
        await client.UseAsync(stockfish, multiPv: 3);
        await using var analysis = await factory.ConnectAnalysisAsync();
        await analysis.AnalyseAsync(1);
        await analysis.WaitForAsync(u => u.RequestId == 1);

        await client.UseAsync(stockfish, multiPv: 1);
        await analysis.AnalyseAsync(2, "e2e4");
        await analysis.WaitForAsync(u => u.RequestId == 2);

        Assert.Equal("setoption name MultiPV value 1", stockfish.Commands().Last(c => c.StartsWith("setoption name MultiPV", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Choosing_another_interactive_analysis_engine_takes_effect_on_the_next_move()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var stockfish = new FakeEngine { Name = "Fake Stockfish" };
        using var lc0 = new FakeEngine { Name = "Fake Lc0" };
        await client.UseAsync(stockfish);
        var leela = await client.AddEngineAsync("Lc0", lc0);
        var host = factory.Services.GetRequiredService<EngineHost>();
        await using var analysis = await factory.ConnectAnalysisAsync();
        await analysis.AnalyseAsync(1);
        await analysis.WaitForAsync(u => u.RequestId == 1);

        await client.SelectEnginesAsync(interactive: leela.Id, analysis: 1);
        await analysis.AnalyseAsync(2, "e2e4");
        await analysis.WaitForAsync(u => u.RequestId == 2);

        Assert.Contains("position startpos moves e2e4", lc0.Commands());
        Assert.Contains("quit", stockfish.Commands());
        Assert.Single(host.RunningProcessIds);
    }

    [Fact]
    public async Task Without_a_configured_Engine_the_Owner_is_told_to_set_the_path()
    {
        using var factory = new CollectionFactory();
        factory.CreateClient();
        await using var analysis = await factory.ConnectAnalysisAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => analysis.AnalyseAsync(1));

        Assert.Contains("Set the Stockfish path in Settings", error.Message);
    }

    [Fact]
    public async Task An_illegal_move_list_is_refused()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine();
        await client.UseAsync(fake);
        await using var analysis = await factory.ConnectAnalysisAsync();

        await Assert.ThrowsAsync<HubException>(() => analysis.AnalyseAsync(1, "e2e5"));
    }

    [Fact]
    public async Task A_line_that_leaves_the_rules_is_cut_at_the_first_illegal_move()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var fake = new FakeEngine().Answer([], "e2e4", "depth 3 multipv 1 score cp 20 pv e2e4 e7e5 e4e5");
        await client.UseAsync(fake, multiPv: 1);
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseAsync(1);

        var update = await analysis.WaitForAsync(u => u.Lines.Count == 1);
        Assert.Equal(["e4", "e5"], update.Lines[0].San);
    }
}

public class PlayLineTests
{
    [Fact]
    public async Task Clicking_an_Engine_line_adds_it_as_a_Variation_at_the_current_position()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync();
        game = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3", "Nc6");
        var afterE5 = game.MainLine()[1];

        var response = await client.PostAsJsonAsync($"/api/games/{game.Id}/moves/line",
            new { parentId = afterE5.Id, moves = new[] { "f1c4", "g8f6", "d2d3" } });

        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<AddMoveResponseDto>())!;
        var updated = result.Game;
        Assert.Equal(["e4", "e5", "Nf3", "Nc6"], updated.MainLine().Select(n => n.San));
        var variation = updated.LineFrom(updated.Node(afterE5.Id).Children[1]);
        Assert.Equal(["Bc4", "Nf6", "d3"], variation.Select(n => n.San));
        Assert.Equal(variation[^1].Id, result.NodeId);
    }

    [Fact]
    public async Task A_line_that_starts_like_an_existing_one_follows_it_instead_of_doubling_it()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync();
        game = await client.PlayMainLineAsync(game.Id, "e4", "e5", "Nf3");

        var response = await client.PostAsJsonAsync($"/api/games/{game.Id}/moves/line",
            new { parentId = (int?)null, moves = new[] { "e2e4", "e7e5", "f1c4" } });

        var updated = (await response.Content.ReadFromJsonAsync<AddMoveResponseDto>())!.Game;
        Assert.Single(updated.FirstMoves);
        var afterE5 = updated.MainLine()[1];
        Assert.Equal(["Nf3", "Bc4"], afterE5.Children.Select(id => updated.Node(id).San));
    }

    [Fact]
    public async Task A_line_with_an_illegal_move_changes_nothing()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        await client.SetOwnerNameAsync("Max Mustermann");
        var game = await client.CreateGameAsync();

        var response = await client.PostAsJsonAsync($"/api/games/{game.Id}/moves/line",
            new { parentId = (int?)null, moves = new[] { "e2e4", "e7e4" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetGameAsync(game.Id)).Nodes);
    }
}
