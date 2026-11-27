using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Engines;

public static class EngineQueries
{
    /// <summary>The Engine Interactive Analysis runs.</summary>
    public static async Task<EngineRecord> InteractiveEngineAsync(this CollectionDbContext db, CancellationToken cancellationToken = default) =>
        await SelectedAsync(db, (await SettingsAsync(db, cancellationToken))?.InteractiveEngineId, cancellationToken);

    /// <summary>The Engine Game Analysis runs, and with it Mistake Training's judging.</summary>
    public static async Task<EngineRecord> AnalysisEngineAsync(this CollectionDbContext db, CancellationToken cancellationToken = default) =>
        await SelectedAsync(db, (await SettingsAsync(db, cancellationToken))?.AnalysisEngineId, cancellationToken);

    /// <summary>A definition by id; null when there is no such Engine.</summary>
    public static Task<EngineRecord?> EngineAsync(this CollectionDbContext db, int id, CancellationToken cancellationToken = default) =>
        db.Engines.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    private static Task<SettingsRecord?> SettingsAsync(CollectionDbContext db, CancellationToken cancellationToken) =>
        db.Settings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The chosen definition, or the first one when none is chosen or the chosen one was removed.
    /// There is always at least one: the last Engine can't be removed.
    /// </summary>
    private static async Task<EngineRecord> SelectedAsync(CollectionDbContext db, int? id, CancellationToken cancellationToken) =>
        (id is { } chosen ? await db.EngineAsync(chosen, cancellationToken) : null)
        ?? await db.Engines.AsNoTracking().OrderBy(e => e.Id).FirstAsync(cancellationToken);

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
