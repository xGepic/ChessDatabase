using ChessDatabase.Api.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace ChessDatabase.Api.Tests;

/// <summary>
/// Runs only when the environment variable STOCKFISH_PATH names a Stockfish executable.
/// Select the category with: dotnet test --filter Category=Stockfish
/// </summary>
public sealed class StockfishFactAttribute : FactAttribute
{
    public static string? Path => Environment.GetEnvironmentVariable("STOCKFISH_PATH");

    public StockfishFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Path)) Skip = "Set STOCKFISH_PATH to run the real Stockfish checks.";
    }
}

[Trait("Category", "Stockfish")]
public class StockfishTests
{
    [StockfishFact]
    public async Task Real_Stockfish_analyses_the_starting_position_and_stops()
    {
        using var factory = new CollectionFactory();
        var client = factory.CreateClient();
        var save = await client.PutEngineAsync(new { path = StockfishFactAttribute.Path, threads = 1, hashMb = 16, multiPv = 3 });
        save.EnsureSuccessStatusCode();
        await using var analysis = await factory.ConnectAnalysisAsync();

        await analysis.AnalyseAsync(1);
        var update = await analysis.WaitForAsync(u => u.Lines.Count == 3 && u.Lines[0].Depth >= 8);
        await analysis.StopAsync();

        Assert.All(update.Lines, l => Assert.InRange(l.Cp ?? 0, -150, 150));
        Assert.Empty(factory.Services.GetRequiredService<EngineHost>().RunningProcessIds);
    }
}
