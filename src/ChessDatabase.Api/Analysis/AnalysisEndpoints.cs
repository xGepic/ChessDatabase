using ChessDatabase.Api.Assessment;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Engines;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Analysis;

/// <param name="SecondsPerMove">Overrides the Game Analysis time per position from Settings.</param>
public sealed record AnalyseRequest(double? SecondsPerMove);

public sealed record QueueRequest(IReadOnlyList<int>? GameIds, double? SecondsPerMove);

/// <summary>A position's evaluation from White's side; Mate 0 means the side to move is checkmated.</summary>
public sealed record AssessedPositionDto(int Ply, double WhitePercent, int? Cp, int? Mate);

/// <param name="NodeId">The move in the Game, or null if the main line has changed there since.</param>
/// <param name="Loss">Winning chance given away, in points (Lichess scale, −100…+100).</param>
/// <param name="BestSan">The Engine's move in the position before, and its line.</param>
public sealed record AssessedMoveDto(
    int Ply, int? NodeId, string San, MoveClassification? Classification, double Loss, double Accuracy,
    string? BestSan, IReadOnlyList<string> BestLineSan, IReadOnlyList<string> BestLineUci);

/// <param name="Stale">The main line has changed since the analysis; moves after the change have no NodeId.</param>
public sealed record EngineAssessmentDto(
    int GameId, string EngineName, int Threads, int HashMb, double SecondsPerMove, DateTime AnalysedAt, bool Stale,
    double? WhiteAccuracy, double? BlackAccuracy, IReadOnlyList<AssessedPositionDto> Positions, IReadOnlyList<AssessedMoveDto> Moves);

public static class AnalysisEndpoints
{
    public const double MinSeconds = 0.1, MaxSeconds = 600;

    public static void MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/games/{id:int}/analysis", async (int id, AnalyseRequest? body, CollectionDbContext db, GameAnalysisQueue queue) =>
        {
            if (!await db.Games.AnyAsync(g => g.Id == id)) return Results.NotFound();
            if (await CheckAsync(db, body?.SecondsPerMove) is { } problem) return problem;
            return Results.Accepted(value: queue.Enqueue([id], ToMs(body?.SecondsPerMove)));
        });

        app.MapGet("/api/games/{id:int}/assessment", async (int id, CollectionDbContext db) =>
            await LoadAsync(db, id) is { } dto ? Results.Ok(dto) : Results.NotFound());

        var queueGroup = app.MapGroup("/api/analysis/queue");
        queueGroup.MapGet("/", (GameAnalysisQueue queue) => queue.Status());
        queueGroup.MapPost("/", async (QueueRequest body, CollectionDbContext db, GameAnalysisQueue queue) =>
        {
            var ids = body.GameIds ?? [];
            if (ids.Count == 0) return Invalid("gameIds", "Choose at least one Game.");
            var known = await db.Games.Where(g => ids.Contains(g.Id)).Select(g => g.Id).ToListAsync();
            if (known.Count != ids.Distinct().Count()) return Invalid("gameIds", "Some of those Games are not in the Collection.");
            if (await CheckAsync(db, body.SecondsPerMove) is { } problem) return problem;
            return Results.Accepted(value: queue.Enqueue(ids, ToMs(body.SecondsPerMove)));
        });
        queueGroup.MapDelete("/", (GameAnalysisQueue queue) => queue.Cancel());
    }

    /// <summary>A problem to report before queueing, or null when Game Analysis can run.</summary>
    private static async Task<IResult?> CheckAsync(CollectionDbContext db, double? secondsPerMove)
    {
        if (secondsPerMove is < MinSeconds or > MaxSeconds)
            return Invalid("secondsPerMove", $"Time per position runs from {MinSeconds} to {MaxSeconds} seconds.");
        var engine = await db.Engines.AsNoTracking().SingleAsync(e => e.Id == EngineRecord.StockfishId);
        return string.IsNullOrWhiteSpace(engine.Path) ? Invalid("engine", $"Set the {engine.Name} path in Settings first.") : null;
    }

    private static int? ToMs(double? seconds) => seconds is { } s ? (int)Math.Round(s * 1000) : null;

    /// <summary>The Game's Engine Assessment, worked out from the stored evaluations, or null if it has none.</summary>
    public static async Task<EngineAssessmentDto?> LoadAsync(CollectionDbContext db, int gameId)
    {
        var record = await db.EngineAssessments.AsNoTracking().SingleOrDefaultAsync(a => a.GameId == gameId);
        if (record is null) return null;
        var nodes = await db.MoveNodes.AsNoTracking().Where(n => n.GameId == gameId).ToListAsync();
        var current = new MoveTree(nodes).MainLine();

        var assessment = Assessor.Assess(
            [.. record.Positions.Select(p => new PositionEvaluation(new EngineScore(p.Cp, p.Mate), p.Line))], record.MainLine);

        var positions = new List<AssessedPositionDto>();
        var moves = new List<AssessedMoveDto>();
        var position = Position.Initial;
        var matching = true;
        for (var ply = 0; ply < record.Positions.Count; ply++)
        {
            var stored = record.Positions[ply];
            var white = new EngineScore(stored.Cp, stored.Mate).ForWhite(position.SideToMove);
            positions.Add(new AssessedPositionDto(ply, Round(assessment.WhitePercents[ply]), white.Cp, white.Mate == 0 ? 0 : white.Mate));
            if (ply == record.MainLine.Count) break;

            var played = Move.TryParseUci(record.MainLine[ply], out var m) ? m : throw new InvalidDataException(record.MainLine[ply]);
            var (bestUci, bestSan) = new UciInfo(0, 1, default, stored.Line).LegalLine(position);
            matching = matching && ply < current.Count && current[ply].Uci == record.MainLine[ply];
            var move = assessment.Moves[ply];
            moves.Add(new AssessedMoveDto(
                ply + 1, matching ? current[ply].Id : null, position.ToSan(played), move.Classification,
                Round(move.Loss), Round(move.Accuracy), bestSan.FirstOrDefault(), bestSan, bestUci));
            position = position.Play(played);
        }

        var stale = !matching || current.Count != record.MainLine.Count;
        return new EngineAssessmentDto(
            gameId, record.EngineName, record.Threads, record.HashMb, record.MsPerMove / 1000.0, record.AnalysedAt, stale,
            RoundOrNull(assessment.WhiteAccuracy), RoundOrNull(assessment.BlackAccuracy), positions, moves);
    }

    private static double Round(double value) => Math.Round(value, 1);

    private static double? RoundOrNull(double? value) => value is { } v ? Round(v) : null;
}
