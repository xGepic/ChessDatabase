using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Engines;

public sealed record EngineDto(int Id, string Name, string Path, int Threads, int HashMb, int MultiPv, string? ReportedName);

/// <param name="Path">The executable; empty clears it. Surrounding quotes (from "Copy as path") are ignored.</param>
/// <param name="Arguments">Leaving it out keeps the stored arguments.</param>
public sealed record SaveEngineRequest(string? Path, string? Arguments, int Threads, int HashMb, int MultiPv);

public static class EngineEndpoints
{
    public const int MaxThreads = 1024;
    public const int MaxHashMb = 262_144;
    public const int MaxLines = 5;

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
            engine.ReportedName = reportedName;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(engine));
        });
    }

    private static EngineDto ToDto(EngineRecord e) =>
        new(e.Id, e.Name, e.Path, e.Threads, e.HashMb, e.MultiPv, e.ReportedName);
}
