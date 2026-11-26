using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Engines;

public static class EngineQueries
{
    /// <summary>The Stockfish definition (it always exists: the first migration seeds it).</summary>
    public static Task<EngineRecord> StockfishAsync(this DbSet<EngineRecord> engines, CancellationToken cancellationToken = default) =>
        engines.AsNoTracking().SingleAsync(e => e.Id == EngineRecord.StockfishId, cancellationToken);

    /// <summary>Why the Engine can't be started as configured, or null when it can be tried.</summary>
    public static string? StartProblem(this EngineRecord engine)
    {
        if (string.IsNullOrWhiteSpace(engine.Path)) return $"Set the {engine.Name} path in Settings first.";
        if (!File.Exists(engine.Path)) return $"There is no file at {engine.Path}. Check the {engine.Name} path in Settings.";
        return null;
    }
}

/// <summary>
/// Time per position for Game Analysis: seconds where the Owner sees it (API, Settings), whole
/// milliseconds where it is stored and sent to the Engine.
/// </summary>
public static class AnalysisTime
{
    public const double MinSeconds = 0.1, MaxSeconds = 600;

    public static string RangeMessage => $"Time per position runs from {MinSeconds} to {MaxSeconds} seconds.";

    public static bool IsValid(double seconds) => seconds is >= MinSeconds and <= MaxSeconds;

    public static int ToMs(double seconds) => (int)Math.Round(seconds * 1000);

    public static double ToSeconds(int ms) => ms / 1000.0;
}
