using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Engines;

/// <param name="InteractiveSecondsLimit">Interactive Analysis search time; null means no limit.</param>
/// <param name="AnalysisSecondsPerMove">Game Analysis time per position.</param>
public sealed record EngineDto(
    int Id, string Name, string Path, int Threads, int HashMb, int MultiPv, int? InteractiveSecondsLimit,
    double AnalysisSecondsPerMove, string? ReportedName);

/// <param name="Path">The executable; empty clears it. Surrounding quotes (from "Copy as path") are ignored.</param>
/// <param name="Arguments">Leaving it out keeps the stored arguments.</param>
/// <param name="InteractiveSecondsLimit">Null means no limit (the default).</param>
/// <param name="AnalysisSecondsPerMove">Leaving it out keeps the stored time.</param>
public sealed record SaveEngineRequest(
    string? Path, string? Arguments, int Threads, int HashMb, int MultiPv,
    int? InteractiveSecondsLimit = null, double? AnalysisSecondsPerMove = null);

public static class EngineEndpoints
{
    public const int MaxThreads = 1024;
    public const int MaxHashMb = 262_144;
    public const int MaxLines = 3;
    public const int MaxSecondsLimit = 3600;

    public static void MapEngineEndpoints(this IEndpointRouteBuilder app)
    {
        var engines = app.MapGroup("/api/engines");

        engines.MapGet("/", async (CollectionDbContext db) =>
            (await db.Engines.AsNoTracking().OrderBy(e => e.Id).ToListAsync()).Select(ToDto));

        engines.MapPut("/{id:int}", async (int id, SaveEngineRequest body, CollectionDbContext db, EngineHost host) =>
        {
            var engine = await db.Engines.FindAsync(id);
            if (engine is null) return Results.NotFound();

            var errors = new Dictionary<string, string[]>();
            if (body.Threads is < 1 or > MaxThreads) errors["threads"] = [$"Threads run from 1 to {MaxThreads}."];
            if (body.HashMb is < 1 or > MaxHashMb) errors["hashMb"] = [$"Hash runs from 1 to {MaxHashMb} MB."];
            if (body.MultiPv is < 1 or > MaxLines) errors["multiPv"] = [$"The Engine shows 1 to {MaxLines} lines."];
            if (body.InteractiveSecondsLimit is < 1 or > MaxSecondsLimit)
                errors["interactiveSecondsLimit"] = [$"The time limit runs from 1 to {MaxSecondsLimit} seconds; leave it empty for none."];
            if (body.AnalysisSecondsPerMove is { } time && !AnalysisTime.IsValid(time))
                errors["analysisSecondsPerMove"] = [AnalysisTime.RangeMessage];

            var path = (body.Path ?? "").Trim().Trim('"').Trim();
            var arguments = body.Arguments ?? engine.Arguments;
            string? reportedName = null;
            if (path.Length > 0 && errors.Count == 0)
            {
                if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
                    errors["path"] = [$"There is no file at {path}."];
                else
                {
                    try
                    {
                        reportedName = await host.ProbeAsync(path, arguments);
                    }
                    catch (EngineException)
                    {
                        errors["path"] = [$"{UciEngine.NotAnEngine} Choose the {engine.Name} program (an .exe file)."];
                    }
                }
            }
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            engine.Path = path;
            engine.Arguments = arguments;
            engine.Threads = body.Threads;
            engine.HashMb = body.HashMb;
            engine.MultiPv = body.MultiPv;
            engine.InteractiveSecondsLimit = body.InteractiveSecondsLimit;
            if (body.AnalysisSecondsPerMove is { } seconds) engine.AnalysisMsPerMove = AnalysisTime.ToMs(seconds);
            engine.ReportedName = reportedName;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(engine));
        });
    }

    private static EngineDto ToDto(EngineRecord e) =>
        new(e.Id, e.Name, e.Path, e.Threads, e.HashMb, e.MultiPv, e.InteractiveSecondsLimit,
            AnalysisTime.ToSeconds(e.AnalysisMsPerMove), e.ReportedName);
}
