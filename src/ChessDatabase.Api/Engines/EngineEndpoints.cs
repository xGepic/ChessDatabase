using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Settings;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Engines;

/// <param name="InteractiveSecondsLimit">Interactive Analysis search time; null means no limit.</param>
/// <param name="AnalysisSecondsPerMove">Game Analysis time per position.</param>
/// <param name="Options">Further UCI options, sent after Threads, Hash and MultiPV.</param>
public sealed record EngineDto(
    int Id, string Name, string Path, int Threads, int HashMb, int MultiPv, int? InteractiveSecondsLimit,
    double AnalysisSecondsPerMove, string? ReportedName, IReadOnlyList<UciOption> Options);

/// <param name="Name">Required when adding; leaving it out keeps the stored one.</param>
/// <param name="Path">The executable; empty clears it. Surrounding quotes (from "Copy as path") are ignored.</param>
/// <param name="Arguments">Leaving it out keeps the stored arguments.</param>
/// <param name="InteractiveSecondsLimit">Null means no limit (the default).</param>
/// <param name="AnalysisSecondsPerMove">Leaving it out keeps the stored time.</param>
/// <param name="Options">Leaving it out keeps the stored options.</param>
public sealed record SaveEngineRequest(
    string? Path, string? Arguments, int Threads, int HashMb, int MultiPv,
    int? InteractiveSecondsLimit = null, double? AnalysisSecondsPerMove = null, string? Name = null,
    IReadOnlyList<UciOption>? Options = null);

/// <summary>Which Engine definition each use runs.</summary>
public sealed record EngineSelectionDto(int InteractiveEngineId, int AnalysisEngineId);

public static class EngineEndpoints
{
    public const int MaxThreads = 1024;
    public const int MaxHashMb = 262_144;
    public const int MaxLines = 3;
    public const int MaxSecondsLimit = 3600;
    public const int MaxOptions = 30, MaxOptionNameLength = 100, MaxOptionValueLength = 1000;

    /// <summary>Options with their own fields, so they can't be given twice.</summary>
    private static readonly string[] ReservedOptions = ["Threads", "Hash", "MultiPV"];

    public static void MapEngineEndpoints(this IEndpointRouteBuilder app)
    {
        var engines = app.MapGroup("/api/engines");

        engines.MapGet("/", async (CollectionDbContext db) =>
            (await db.Engines.AsNoTracking().OrderBy(e => e.Id).ToListAsync()).Select(ToDto));

        engines.MapPost("/", async (SaveEngineRequest body, CollectionDbContext db, EngineHost host) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name)) return Invalid("name", "Give the Engine a name, e.g. Lc0.");
            var engine = new EngineRecord { AnalysisMsPerMove = EngineRecord.Stockfish().AnalysisMsPerMove };
            if (await ApplyAsync(engine, body, db, host) is { } problem) return problem;
            db.Engines.Add(engine);
            await db.SaveChangesAsync();
            return Results.Created($"/api/engines/{engine.Id}", ToDto(engine));
        });

        engines.MapPut("/{id:int}", async (int id, SaveEngineRequest body, CollectionDbContext db, EngineHost host) =>
        {
            var engine = await db.Engines.FindAsync(id);
            if (engine is null) return Results.NotFound();
            if (await ApplyAsync(engine, body, db, host) is { } problem) return problem;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(engine));
        });

        // The uses of a removed Engine fall back to the first remaining one (see EngineQueries).
        engines.MapDelete("/{id:int}", async (int id, CollectionDbContext db) =>
        {
            var engine = await db.Engines.FindAsync(id);
            if (engine is null) return Results.NotFound();
            if (await db.Engines.CountAsync() == 1) return Invalid("engine", "The last Engine can't be removed.");
            db.Engines.Remove(engine);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        engines.MapGet("/selection", async (CollectionDbContext db) =>
            new EngineSelectionDto((await db.InteractiveEngineAsync()).Id, (await db.AnalysisEngineAsync()).Id));

        engines.MapPut("/selection", async (EngineSelectionDto body, CollectionDbContext db) =>
        {
            var known = await db.Engines.Select(e => e.Id).ToListAsync();
            var errors = new Dictionary<string, string[]>();
            if (!known.Contains(body.InteractiveEngineId)) errors["interactiveEngineId"] = ["There is no such Engine."];
            if (!known.Contains(body.AnalysisEngineId)) errors["analysisEngineId"] = ["There is no such Engine."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var settings = await SettingsEndpoints.LoadAsync(db);
            settings.InteractiveEngineId = body.InteractiveEngineId;
            settings.AnalysisEngineId = body.AnalysisEngineId;
            await db.SaveChangesAsync();
            return Results.Ok(body);
        });
    }

    /// <summary>Checks the request and copies it onto the definition; a 400 problem when it is not valid.</summary>
    private static async Task<IResult?> ApplyAsync(EngineRecord engine, SaveEngineRequest body, CollectionDbContext db, EngineHost host)
    {
        var errors = new Dictionary<string, string[]>();
        var name = body.Name?.Trim() ?? engine.Name;
        if (name.Length is 0 or > 100) errors["name"] = ["An Engine name has 1 to 100 characters."];
        else if (await db.Engines.AnyAsync(e => e.Id != engine.Id && e.Name.ToLower() == name.ToLower()))
            errors["name"] = [$"There is already an Engine called {name}."];
        if (body.Threads is < 1 or > MaxThreads) errors["threads"] = [$"Threads run from 1 to {MaxThreads}."];
        if (body.HashMb is < 1 or > MaxHashMb) errors["hashMb"] = [$"Hash runs from 1 to {MaxHashMb} MB."];
        if (body.MultiPv is < 1 or > MaxLines) errors["multiPv"] = [$"The Engine shows 1 to {MaxLines} lines."];
        if (body.InteractiveSecondsLimit is < 1 or > MaxSecondsLimit)
            errors["interactiveSecondsLimit"] = [$"The time limit runs from 1 to {MaxSecondsLimit} seconds; leave it empty for none."];
        if (body.AnalysisSecondsPerMove is { } time && !AnalysisTime.IsValid(time))
            errors["analysisSecondsPerMove"] = [AnalysisTime.RangeMessage];
        var options = body.Options?.Select(o => new UciOption(o.Name?.Trim() ?? "", o.Value?.Trim() ?? "")).ToList() ?? engine.Options;
        if (OptionsProblem(options) is { } optionsProblem) errors["options"] = [optionsProblem];

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
                    errors["path"] = [$"{UciEngine.NotAnEngine} Choose the {name} program (an .exe file)."];
                }
            }
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        engine.Name = name;
        engine.Path = path;
        engine.Arguments = arguments;
        engine.Threads = body.Threads;
        engine.HashMb = body.HashMb;
        engine.MultiPv = body.MultiPv;
        engine.InteractiveSecondsLimit = body.InteractiveSecondsLimit;
        if (body.AnalysisSecondsPerMove is { } seconds) engine.AnalysisMsPerMove = AnalysisTime.ToMs(seconds);
        engine.ReportedName = reportedName;
        engine.Options = options;
        return null;
    }

    /// <summary>Null when the options can be sent as they are, otherwise why not.</summary>
    private static string? OptionsProblem(IReadOnlyList<UciOption> options)
    {
        if (options.Count > MaxOptions) return $"An Engine takes at most {MaxOptions} further options.";
        foreach (var (name, value) in options)
        {
            if (name.Length == 0) return "Every option needs a name.";
            if (name.Length > MaxOptionNameLength || value.Length > MaxOptionValueLength)
                return $"Option names have at most {MaxOptionNameLength} characters and values at most {MaxOptionValueLength}.";
            if (ReservedOptions.Contains(name, StringComparer.OrdinalIgnoreCase))
                return $"{name} has its own field above.";
            // One option is one line of UCI; a line break would send a command of its own.
            if (name.Contains('\n') || name.Contains('\r') || value.Contains('\n') || value.Contains('\r'))
                return "Options can't contain a line break.";
            if (name.Split(' ').Contains("value", StringComparer.OrdinalIgnoreCase))
                return "An option name can't contain the word \"value\".";
        }
        var duplicate = options.GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        return duplicate is null ? null : $"{duplicate.Key} is given twice.";
    }

    private static EngineDto ToDto(EngineRecord e) =>
        new(e.Id, e.Name, e.Path, e.Threads, e.HashMb, e.MultiPv, e.InteractiveSecondsLimit,
            AnalysisTime.ToSeconds(e.AnalysisMsPerMove), e.ReportedName, e.Options);
}
