using System.Collections.Concurrent;
using ChessDatabase.Api.Collection;

namespace ChessDatabase.Api.Engines;

/// <summary>
/// Starts and tracks every UCI engine process the app runs. When the app shuts down, each one is
/// told to quit and killed if it doesn't, so no Engine keeps a CPU busy after the app is gone.
/// (If the app is killed outright, Stockfish still exits: its input closes.)
/// </summary>
public sealed class EngineHost : IAsyncDisposable
{
    private readonly ConcurrentDictionary<UciEngine, int> _running = new();

    /// <summary>Process ids of the engines running now, for diagnostics and tests.</summary>
    public IReadOnlyCollection<int> RunningProcessIds => [.. _running.Values];

    /// <summary>Starts the Engine and applies its options (Threads, Hash, MultiPV).</summary>
    public async Task<UciEngine> StartAsync(EngineRecord definition, int multiPv, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(definition.Path))
            throw new EngineException($"Set the {definition.Name} path in Settings first.");
        if (!File.Exists(definition.Path))
            throw new EngineException($"There is no file at {definition.Path}. Check the {definition.Name} path in Settings.");

        var engine = await UciEngine.StartAsync(definition.Path, definition.Arguments, cancellationToken);
        _running[engine] = engine.ProcessId;
        engine.Disposed += e => _running.TryRemove(e, out _);
        try
        {
            await engine.ConfigureAsync(
                [("Threads", definition.Threads), ("Hash", definition.HashMb), ("MultiPV", multiPv)], cancellationToken);
            return engine;
        }
        catch
        {
            await engine.DisposeAsync();
            throw;
        }
    }

    /// <summary>Starts the program only to check that it speaks UCI; returns the name it reports.</summary>
    public async Task<string> ProbeAsync(string path, string? arguments)
    {
        await using var engine = await UciEngine.StartAsync(path, arguments);
        return engine.Name;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_running.Keys.Select(e => e.DisposeAsync().AsTask()));
    }
}
