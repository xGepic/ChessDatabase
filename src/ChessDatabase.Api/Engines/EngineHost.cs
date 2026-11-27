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

    /// <summary>Starts the Engine and applies its options: Threads, Hash, MultiPV and the further ones.</summary>
    /// <param name="threads">Overrides the definition's threads (two Engines side by side share the CPU).</param>
    public async Task<UciEngine> StartAsync(
        EngineRecord definition, int multiPv, int? threads = null, CancellationToken cancellationToken = default)
    {
        if (definition.StartProblem() is { } problem) throw new EngineException(problem);

        var engine = Track(await UciEngine.StartAsync(definition.Path, definition.Arguments, cancellationToken));
        try
        {
            await engine.ConfigureAsync(
                [("Threads", threads ?? definition.Threads), ("Hash", definition.HashMb), ("MultiPV", multiPv),
                 .. definition.Options.Select(o => (o.Name, (object)o.Value))],
                cancellationToken);
            return engine;
        }
        catch (EngineException e)
        {
            await engine.DisposeAsync();
            throw new EngineException(
                $"{definition.Name} {e.Message}. Check its options in Settings (for Lc0, the weights file and backend).", e);
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
        await using var engine = Track(await UciEngine.StartAsync(path, arguments));
        return engine.Name;
    }

    /// <summary>Every process is tracked from its start, so shutting down reaps it too.</summary>
    private UciEngine Track(UciEngine engine)
    {
        _running[engine] = engine.ProcessId;
        engine.Disposed += e => _running.TryRemove(e, out _);
        return engine;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_running.Keys.Select(e => e.DisposeAsync().AsTask()));
    }
}
