using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;

namespace ChessDatabase.Api.Tests;

/// <summary>With two Engines configured, each use starts the one chosen for it.</summary>
public class EngineRoutingTests
{
    private static FakeEngine Lc0() =>
        new() { Name = "Fake Lc0", Options = ["Threads", "MultiPV", "WeightsFile", "Backend"] };

    private static readonly object[] Lc0Options =
        [new { name = "WeightsFile", value = @"C:\Nets\BT4.pb.gz" }, new { name = "Backend", value = "cuda-fp16" }];

    private static bool Searched(FakeEngine engine) => engine.Commands().Any(c => c.StartsWith("go", StringComparison.Ordinal));

    [Fact]
    public async Task Game_Analysis_runs_the_engine_chosen_for_it_and_the_assessment_names_it()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var stockfish = new FakeEngine { Name = "Fake Stockfish" };
        using var lc0 = Lc0();
        await client.UseAsync(stockfish);
        var leela = await client.AddEngineAsync("Lc0", lc0, options: Lc0Options);
        await client.SelectEnginesAsync(interactive: 1, analysis: leela.Id);
        var game = await GameAnalysisTests.CreateScholarsMateAsync(client);

        (await client.PostAsJsonAsync($"/api/games/{game.Id}/analysis", new { })).EnsureSuccessStatusCode();
        var assessment = await client.WaitForAssessmentAsync(game.Id);

        Assert.Equal("Fake Lc0", assessment.EngineName);
        Assert.True(Searched(lc0));
        Assert.False(Searched(stockfish));
    }

    [Fact]
    public async Task Interactive_Analysis_runs_the_engine_chosen_for_it()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var stockfish = new FakeEngine { Name = "Fake Stockfish" };
        using var lc0 = Lc0();
        await client.UseAsync(stockfish);
        var leela = await client.AddEngineAsync("Lc0", lc0, options: Lc0Options);
        await client.SelectEnginesAsync(interactive: leela.Id, analysis: 1);
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseAsync(1, "e2e4");
        await analysis.WaitForAsync(u => u.RequestId == 1 && u.Lines.Count > 0);

        Assert.True(Searched(lc0));
        Assert.False(Searched(stockfish));
    }

    [Fact]
    public async Task Only_the_options_an_engine_announces_are_sent_so_Lc0_gets_its_weights_and_backend_but_no_hash()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var lc0 = Lc0();
        var leela = await client.AddEngineAsync("Lc0", lc0, threads: 2, options: Lc0Options);
        await client.SelectEnginesAsync(interactive: leela.Id, analysis: leela.Id);
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseAsync(1);
        await analysis.WaitForAsync(u => u.RequestId == 1);

        var setoptions = lc0.Commands().Where(c => c.StartsWith("setoption", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            ["setoption name Threads value 2", "setoption name MultiPV value 3",
             @"setoption name WeightsFile value C:\Nets\BT4.pb.gz", "setoption name Backend value cuda-fp16"],
            setoptions);
    }

    [Fact]
    public async Task An_engine_that_quits_while_loading_its_options_says_so()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        using var lc0 = new FakeEngine { Name = "Fake Lc0", QuitOnIsReady = true };
        var leela = await client.AddEngineAsync("Lc0", lc0, options: Lc0Options);
        await client.SelectEnginesAsync(interactive: leela.Id, analysis: 1);
        await using var analysis = await factory.ConnectAnalysisAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => analysis.AnalyseAsync(1));

        Assert.Contains("Lc0 stopped while loading its options", error.Message);
    }
}
