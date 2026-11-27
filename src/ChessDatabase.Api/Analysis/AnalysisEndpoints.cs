using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Engines;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Analysis;

/// <param name="SecondsPerMove">Overrides the Game Analysis time per position from Settings.</param>
public sealed record AnalyseRequest(double? SecondsPerMove);

public sealed record QueueRequest(IReadOnlyList<int>? GameIds, double? SecondsPerMove);

public static class AnalysisEndpoints
{
    public static void MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/games/{id:int}/analysis", async (int id, AnalyseRequest? body, CollectionDbContext db, GameAnalysisQueue queue) =>
        {
            if (!await db.Games.AnyAsync(g => g.Id == id)) return Results.NotFound();
            if (await WhyAnalysisCannotStartAsync(db, body?.SecondsPerMove) is { } problem) return problem;
            return Results.Accepted(value: await queue.EnqueueAsync([id], ToMs(body?.SecondsPerMove)));
        });

        app.MapGet("/api/games/{id:int}/assessment", async (int id, CollectionDbContext db) =>
            await AssessmentQueries.LoadAsync(db, id) is { } dto ? Results.Ok(dto) : Results.NotFound());

        var queueGroup = app.MapGroup("/api/analysis/queue");
        queueGroup.MapGet("/", (GameAnalysisQueue queue) => queue.Status());
        queueGroup.MapPost("/", async (QueueRequest body, CollectionDbContext db, GameAnalysisQueue queue) =>
        {
            var ids = body.GameIds ?? [];
            if (ids.Count == 0) return Invalid("gameIds", "Choose at least one Game.");
            var known = await db.Games.Where(g => ids.Contains(g.Id)).Select(g => g.Id).ToListAsync();
            if (known.Count != ids.Distinct().Count()) return Invalid("gameIds", "Some of those Games are not in the Collection.");
            if (await WhyAnalysisCannotStartAsync(db, body.SecondsPerMove) is { } problem) return problem;
            return Results.Accepted(value: await queue.EnqueueAsync(ids, ToMs(body.SecondsPerMove)));
        });
        queueGroup.MapDelete("/", (GameAnalysisQueue queue) => queue.CancelAsync());
    }

    /// <summary>A 400 problem to report before queueing, or null when Game Analysis can run.</summary>
    private static async Task<IResult?> WhyAnalysisCannotStartAsync(CollectionDbContext db, double? secondsPerMove)
    {
        if (secondsPerMove is { } seconds && !AnalysisTime.IsValid(seconds))
            return Invalid("secondsPerMove", AnalysisTime.RangeMessage);
        return (await db.AnalysisEngineAsync()).StartProblem() is { } problem ? Invalid("engine", problem) : null;
    }

    private static int? ToMs(double? seconds) => seconds is { } s ? AnalysisTime.ToMs(s) : null;
}
