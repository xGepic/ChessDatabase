using ChessDatabase.Api.Assessment;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Engines;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Analysis;

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
    int GameId, string EngineName, int Threads, int HashMb, double SecondsPerMove, DateTimeOffset AnalysedAt, bool Stale,
    double? WhiteAccuracy, double? BlackAccuracy, IReadOnlyList<AssessedPositionDto> Positions, IReadOnlyList<AssessedMoveDto> Moves);

/// <summary>Reading a Game's Engine Assessment the way the API shows it.</summary>
public static class AssessmentQueries
{
    /// <summary>Classifications and Accuracy worked out from the stored evaluations.</summary>
    public static GameAssessment Assess(EngineAssessmentRecord record) =>
        Assessor.Assess([.. record.Positions.Select(p => new PositionEvaluation(new EngineScore(p.Cp, p.Mate), p.Line))], record.MainLine);

    /// <summary>
    /// The Game's Engine Assessment, worked out from the stored evaluations and matched to the
    /// Game's current main line, or null if it has none.
    /// </summary>
    public static async Task<EngineAssessmentDto?> LoadAsync(CollectionDbContext db, int gameId)
    {
        var record = await db.EngineAssessments.AsNoTracking().SingleOrDefaultAsync(a => a.GameId == gameId);
        if (record is null) return null;
        var nodes = await db.MoveNodes.AsNoTracking().Where(n => n.GameId == gameId).ToListAsync();
        var current = new MoveTree(nodes).MainLine();

        var assessment = Assess(record);

        var positions = new List<AssessedPositionDto>();
        var moves = new List<AssessedMoveDto>();
        var line = Line.FromUci(record.MainLine);
        var matching = true;
        for (var ply = 0; ply < record.Positions.Count; ply++)
        {
            var position = line.Positions[ply];
            var stored = record.Positions[ply];
            var white = new EngineScore(stored.Cp, stored.Mate).ForWhite(position.SideToMove);
            positions.Add(new AssessedPositionDto(ply, Round(assessment.WhitePercents[ply]), white.Cp, white.Mate == 0 ? 0 : white.Mate));
            if (ply == record.MainLine.Count) break;

            var (bestMoves, bestSan) = position.PlayableLine(stored.Line);
            matching = matching && ply < current.Count && current[ply].Uci == record.MainLine[ply];
            var move = assessment.Moves[ply];
            moves.Add(new AssessedMoveDto(
                ply + 1, matching ? current[ply].Id : null, line.San[ply], move.Classification,
                Round(move.Loss), Round(move.Accuracy), bestSan is [var best, ..] ? best : null, bestSan, [.. bestMoves.Select(b => b.ToUci())]));
        }

        var stale = !matching || current.Count != record.MainLine.Count;
        return new EngineAssessmentDto(
            gameId, record.EngineName, record.Threads, record.HashMb, AnalysisTime.ToSeconds(record.MsPerMove),
            new DateTimeOffset(DateTime.SpecifyKind(record.AnalysedAt, DateTimeKind.Utc)), stale,
            RoundOrNull(assessment.WhiteAccuracy), RoundOrNull(assessment.BlackAccuracy), positions, moves);
    }

    private static double Round(double value) => Math.Round(value, 1);

    private static double? RoundOrNull(double? value) => value is { } v ? Round(v) : null;
}
