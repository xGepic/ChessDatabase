using System.Diagnostics;
using ChessDatabase.Api.Engines;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace ChessDatabase.Api.Tests;

/// <summary>Interactive Analysis with two Engines at once, each streaming its own lines.</summary>
public class SideBySideAnalysisTests
{
    private static readonly string[] AfterE4 = ["e2e4"];

    /// <summary>Stockfish (16 threads) as the Interactive Analysis engine and Lc0 (2 threads) beside it.</summary>
    private static async Task<(FakeEngine Stockfish, FakeEngine Lc0, int Lc0Id)> TwoEnginesAsync(HttpClient client)
    {
        var stockfish = new FakeEngine { Name = "Fake Stockfish" }
            .Answer(AfterE4, "c7c5", "depth 20 multipv 1 score cp -30 pv c7c5 g1f3");
        var lc0 = new FakeEngine { Name = "Fake Lc0", Options = ["Threads", "MultiPV", "WeightsFile"] }
            .Answer(AfterE4, "e7e5", "depth 9 multipv 1 score cp -12 pv e7e5 g1f3");
        await client.UseAsync(stockfish, threads: 16, multiPv: 1);
        var added = await client.AddEngineAsync("Lc0", lc0, threads: 2, multiPv: 1);
        return (stockfish, lc0, added.Id);
    }

    private static string? ThreadsSent(FakeEngine engine) =>
        engine.Commands().LastOrDefault(c => c.StartsWith("setoption name Threads", StringComparison.Ordinal));

    [Fact]
    public async Task Both_engines_stream_their_own_lines_for_the_position()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var (stockfish, lc0, lc0Id) = await TwoEnginesAsync(client);
        using var _ = stockfish;
        using var __ = lc0;
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseSideBySideAsync(1, lc0Id, "e2e4");

        // Scores are from White's side: Black's -30 after 1.e4 is +30.
        var fromStockfish = await analysis.WaitForAsync(u => u.RequestId == 1 && u.EngineId == 1 && u.Lines.Count > 0);
        var fromLc0 = await analysis.WaitForAsync(u => u.RequestId == 1 && u.EngineId == lc0Id && u.Lines.Count > 0);
        Assert.Equal((30, "c5"), (fromStockfish.Lines[0].Cp, fromStockfish.Lines[0].San[0]));
        Assert.Equal((12, "e5"), (fromLc0.Lines[0].Cp, fromLc0.Lines[0].San[0]));
    }

    [Fact]
    public async Task Both_follow_the_position_and_both_stop_when_switched_off()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var (stockfish, lc0, lc0Id) = await TwoEnginesAsync(client);
        using var _ = stockfish;
        using var __ = lc0;
        var host = factory.Services.GetRequiredService<EngineHost>();
        await using var analysis = await factory.ConnectAnalysisAsync();
        await analysis.AnalyseSideBySideAsync(1, lc0Id);

        await analysis.AnalyseSideBySideAsync(2, lc0Id, "e2e4");
        await analysis.WaitForAsync(u => u.RequestId == 2 && u.EngineId == 1);
        await analysis.WaitForAsync(u => u.RequestId == 2 && u.EngineId == lc0Id);
        Assert.Equal(2, host.RunningProcessIds.Count);
        Assert.All([stockfish, lc0], e => Assert.Contains("position startpos moves e2e4", e.Commands()));

        var processes = host.RunningProcessIds.Select(Process.GetProcessById).ToList();
        await analysis.StopAsync();

        Assert.Empty(host.RunningProcessIds);
        Assert.All(processes, p => Assert.True(p.WaitForExit(5000)));
    }

    [Fact]
    public async Task Leaving_the_Game_ends_both_engines()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var (stockfish, lc0, lc0Id) = await TwoEnginesAsync(client);
        using var _ = stockfish;
        using var __ = lc0;
        var host = factory.Services.GetRequiredService<EngineHost>();
        var analysis = await factory.ConnectAnalysisAsync();
        await analysis.AnalyseSideBySideAsync(1, lc0Id);
        await analysis.WaitForAsync(u => u.EngineId == lc0Id);

        await analysis.DisposeAsync();

        await Wait.UntilAsync(() => host.RunningProcessIds.Count == 0, "both Engines end with the connection");
    }

    [Fact]
    public async Task Side_by_side_the_engines_share_the_cpu_threads_and_alone_each_has_its_own()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var (stockfish, lc0, lc0Id) = await TwoEnginesAsync(client);
        using var _ = stockfish;
        using var __ = lc0;
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseAsync(1);
        await analysis.WaitForAsync(u => u.RequestId == 1);
        Assert.Equal("setoption name Threads value 16", ThreadsSent(stockfish));

        await analysis.AnalyseSideBySideAsync(2, lc0Id);
        await analysis.WaitForAsync(u => u.RequestId == 2 && u.EngineId == 1);
        await analysis.WaitForAsync(u => u.RequestId == 2 && u.EngineId == lc0Id);
        Assert.Equal("setoption name Threads value 14", ThreadsSent(stockfish)); // Lc0 mostly uses the GPU
        Assert.Equal("setoption name Threads value 2", ThreadsSent(lc0));

        await analysis.AnalyseAsync(3);
        await analysis.WaitForAsync(u => u.RequestId == 3);
        Assert.Equal("setoption name Threads value 16", ThreadsSent(stockfish));
    }

    [Fact]
    public async Task A_second_engine_that_fails_leaves_the_first_running()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var stockfish = new FakeEngine { Name = "Fake Stockfish" };
        await client.UseAsync(stockfish);
        using var broken = new FakeEngine { Name = "Fake Lc0", QuitOnIsReady = true };
        var lc0 = await client.AddEngineAsync("Lc0", broken);
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseSideBySideAsync(1, lc0.Id);

        await analysis.WaitForAsync(u => u.RequestId == 1 && u.EngineId == 1);
        var (message, engineId) = Assert.Single(analysis.EngineErrors);
        Assert.Equal(lc0.Id, engineId);
        Assert.Contains("Lc0 stopped while loading", message);
    }

    [Fact]
    public async Task A_removed_second_engine_only_fails_its_own_panel()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var (stockfish, lc0, lc0Id) = await TwoEnginesAsync(client);
        using var _ = stockfish;
        using var __ = lc0;
        (await client.DeleteAsync($"/api/engines/{lc0Id}")).EnsureSuccessStatusCode();
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseSideBySideAsync(1, lc0Id, "e2e4");

        await analysis.WaitForAsync(u => u.RequestId == 1 && u.EngineId == 1 && u.Lines.Count > 0);
        Assert.Equal(lc0Id, Assert.Single(analysis.EngineErrors).EngineId);
    }

    [Fact]
    public async Task A_second_engine_that_failed_is_not_restarted_on_every_move()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var stockfish = new FakeEngine { Name = "Fake Stockfish" };
        await client.UseAsync(stockfish);
        using var broken = new FakeEngine { Name = "Fake Lc0", QuitOnIsReady = true };
        var lc0 = await client.AddEngineAsync("Lc0", broken);
        int Starts() => broken.Commands().Count(c => c == "isready");
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseSideBySideAsync(1, lc0.Id);
        await analysis.AnalyseSideBySideAsync(2, lc0.Id, "e2e4");
        await analysis.WaitForAsync(u => u.RequestId == 2 && u.EngineId == 1);

        Assert.Equal(1, Starts());
        Assert.Single(analysis.EngineErrors);

        await analysis.AnalyseAsync(3);                     // the comparison is closed...
        await analysis.AnalyseSideBySideAsync(4, lc0.Id);   // ...and chosen again: one more try
        await analysis.WaitForAsync(u => u.RequestId == 4 && u.EngineId == 1);
        Assert.Equal(2, Starts());
    }

    [Fact]
    public async Task Changing_a_running_engine_in_settings_restarts_it_with_the_new_settings()
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
    public async Task Analyse_answers_with_the_interactive_analysis_engine_it_ran_so_the_view_follows_a_change_in_settings()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var (stockfish, lc0, lc0Id) = await TwoEnginesAsync(client);
        using var _ = stockfish;
        using var __ = lc0;
        await using var analysis = await factory.ConnectAnalysisAsync();

        var first = await analysis.Connection.InvokeAsync<int>("Analyse", 1, Array.Empty<string>(), (int?)null);
        await client.SelectEnginesAsync(interactive: lc0Id, analysis: 1);
        var second = await analysis.Connection.InvokeAsync<int>("Analyse", 2, Array.Empty<string>(), (int?)null);

        Assert.Equal((1, lc0Id), (first, second));
    }

    [Theory]
    [InlineData(16, 2, 14, 2)]
    [InlineData(2, 16, 2, 14)]
    [InlineData(16, 16, 8, 8)]
    [InlineData(16, 12, 8, 8)]
    [InlineData(1, 1, 1, 1)]
    public void The_engine_with_fewer_threads_keeps_up_to_half_and_the_other_gets_the_rest(int a, int b, int sharedA, int sharedB)
    {
        Assert.Equal((sharedA, sharedB), ThreadShare.SideBySide(a, b));
    }
}
