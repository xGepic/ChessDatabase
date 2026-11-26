using System.Collections.Concurrent;
using ChessDatabase.Api.Analysis;
using ChessDatabase.Api.Assessment;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Engines;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Training;

/// <summary>A position from one of the Owner's Games where the Owner made a mistake or blunder.</summary>
/// <param name="Ply">The half-move of the Owner's mistake: open the Game at <c>Ply − 1</c> to see the position.</param>
/// <param name="Fen">The position before the mistake: the Owner is to move.</param>
/// <param name="LastMove">The Opponent's move that led to it (UCI); null at the start.</param>
/// <param name="PlayedSan">What the Owner played in the Game.</param>
/// <param name="Opponent">The Opponent's name in that Game.</param>
/// <param name="Date">The Game's date as recorded, if any.</param>
/// <param name="New">Never answered before.</param>
public sealed record MistakeItemDto(
    int ItemId, int GameId, int Ply, OwnerColour Side, string Fen, string? LastMove, string PlayedSan,
    MoveClassification Classification, string Opponent, string? Date, bool New);

/// <param name="Correct">The Engine's best move, or a move that loses less than an inaccuracy would.</param>
/// <param name="BestLineSan">The Engine's best line from the position, shown after answering.</param>
/// <param name="Shown">The move to show on the board: the Owner's when right, the Engine's best when wrong.</param>
/// <param name="FenAfter">The position after <paramref name="Shown"/>.</param>
/// <param name="DueAt">When the item comes back.</param>
public sealed record MistakeAnswerDto(
    bool Correct, IReadOnlyList<string> BestLineSan, IReadOnlyList<string> BestLineUci, string Shown, string FenAfter, DateTimeOffset DueAt);

/// <summary>
/// A move an Engine Assessment classifies as a mistake or blunder, either side's.
/// </summary>
/// <param name="Ply">The move's half-move (1 = White's first).</param>
/// <param name="MainLine">The analysed main line (UCI).</param>
/// <param name="Before">The position the move was played in.</param>
/// <param name="Evaluation">The Engine's evaluation of <paramref name="Before"/> (from the side to move) and its best line.</param>
/// <param name="MsPerMove">The time the Engine had per position in that analysis.</param>
public sealed record AnalysedMistake(
    int GameId, int Ply, IReadOnlyList<string> MainLine, Position Before, string PlayedSan,
    MoveClassification Classification, StoredEvaluation Evaluation, int MsPerMove)
{
    /// <summary>The Game and the position (as the position index keys it): the same position after re-running Game Analysis is the same item.</summary>
    public string Key => $"{GameId}:{PositionIndex.Key(Before)}";

    public Color Mover => Before.SideToMove;
}

/// <summary>
/// The mistakes and blunders of each Engine Assessment, worked out once per analysis (replaying
/// and assessing a Game is the costly part) and kept for the app's run.
/// </summary>
public sealed class AnalysedMistakes
{
    private readonly ConcurrentDictionary<int, (DateTime AnalysedAt, IReadOnlyList<AnalysedMistake> Mistakes)> _byAssessment = new();

    /// <summary>Every analysed mistake and blunder, either side's, newest Game first.</summary>
    public async Task<List<AnalysedMistake>> AllAsync(CollectionDbContext db)
    {
        var current = await db.EngineAssessments.AsNoTracking()
            .Select(a => new { a.Id, a.AnalysedAt }).ToListAsync();
        var missing = current.Where(a => !_byAssessment.TryGetValue(a.Id, out var known) || known.AnalysedAt != a.AnalysedAt)
            .Select(a => a.Id).ToList();
        foreach (var record in await db.EngineAssessments.AsNoTracking().Where(a => missing.Contains(a.Id)).ToListAsync())
            _byAssessment[record.Id] = (record.AnalysedAt, Find(record));
        foreach (var gone in _byAssessment.Keys.Except(current.Select(a => a.Id)).ToList())
            _byAssessment.TryRemove(gone, out _);
        return [.. _byAssessment.Values.SelectMany(v => v.Mistakes).OrderByDescending(m => m.GameId).ThenBy(m => m.Ply)];
    }

    private static List<AnalysedMistake> Find(EngineAssessmentRecord record)
    {
        var assessment = AssessmentQueries.Assess(record);
        var mistakes = new List<AnalysedMistake>();
        var position = Position.Initial;
        for (var i = 0; i < record.MainLine.Count; i++)
        {
            var move = Move.TryParseUci(record.MainLine[i], out var parsed) ? parsed : throw new InvalidDataException(record.MainLine[i]);
            if (assessment.Moves[i].Classification is MoveClassification.Mistake or MoveClassification.Blunder)
                mistakes.Add(new AnalysedMistake(
                    record.GameId, i + 1, record.MainLine, position, position.ToSan(move),
                    assessment.Moves[i].Classification!.Value, record.Positions[i], record.MsPerMove));
            position = position.Play(move);
        }
        return mistakes;
    }
}

/// <summary>
/// Mistake Training: every move of the Owner that an Engine Assessment classifies as a mistake or
/// a blunder is an item, the position before it a puzzle: find a better move. Any move that loses
/// less winning chance than an inaccuracy would (compared with the Engine's best) counts as right.
/// Only mistakes in moves the Game still has count: after an edit before a mistake, it waits for
/// Game Analysis to run again. Items are keyed by Game and position, so re-running Game Analysis
/// keeps the progress of positions that are still mistakes.
/// </summary>
public static class MistakeTrainingEndpoints
{
    /// <summary>Maps /api/training/mistakes: the summary, /next and /{itemId}/answer.</summary>
    public static void MapMistakeTrainingEndpoints(this IEndpointRouteBuilder app)
    {
        var training = app.MapGroup("/api/training/mistakes");

        training.MapGet("/", async (CollectionDbContext db, AnalysedMistakes analysed, TimeProvider clock) =>
            Results.Ok(TrainingItems.Summary((await SyncAsync(db, analysed, clock)).Items, clock)));

        // The item due first, or 204 when nothing is due today.
        training.MapGet("/next", async (CollectionDbContext db, AnalysedMistakes analysed, TimeProvider clock) =>
        {
            var (items, mistakes) = await SyncAsync(db, analysed, clock);
            if (TrainingItems.NextDue(items, clock) is not { } next) return Results.NoContent();
            var mistake = mistakes[next.Key];
            var game = await db.Games.AsNoTracking().Include(g => g.Opponent).SingleAsync(g => g.Id == mistake.GameId);
            return Results.Ok(new MistakeItemDto(
                next.Id, mistake.GameId, mistake.Ply, game.OwnerColour, mistake.Before.ToFen(),
                mistake.Ply > 1 ? mistake.MainLine[mistake.Ply - 2] : null, mistake.PlayedSan, mistake.Classification,
                game.Opponent.Name, game.Date, next.ReviewedAt is null));
        });

        training.MapPost("/{itemId:int}/answer", async (
            int itemId, TrainingAnswerRequest body, CollectionDbContext db, AnalysedMistakes analysed, TimeProvider clock,
            GameAnalyzer analyzer, CancellationToken cancellationToken) =>
        {
            var (items, mistakes) = await SyncAsync(db, analysed, clock);
            if (items.FirstOrDefault(i => i.Id == itemId) is not { } item) return Results.NotFound();
            var mistake = mistakes[item.Key];
            if (!MoveTreeEndpoints.TryReadMove(mistake.Before, body.Move, out var move))
                return Invalid("move", $"'{body.Move}' is not a legal move here.");

            bool right;
            try
            {
                right = await IsRightAsync(mistake, move, analyzer, cancellationToken);
            }
            catch (EngineException e)
            {
                return Results.Problem($"The Engine is needed to judge that move: {e.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            SpacedRepetition.Review(item, right, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            var (bestMoves, bestSan) = mistake.Before.PlayableLine(mistake.Evaluation.Line);
            var shown = right || bestMoves.Count == 0 ? move : bestMoves[0];
            return Results.Ok(new MistakeAnswerDto(
                right, bestSan, [.. bestMoves.Select(b => b.ToUci())], shown.ToUci(), mistake.Before.Play(shown).ToFen(),
                new DateTimeOffset(item.DueAt, TimeSpan.Zero)));
        });
    }

    /// <summary>
    /// The best move is right; the Game's own move is wrong; any other move is evaluated by the
    /// Engine with the analysis's own time per position and is right when it loses less than an
    /// inaccuracy.
    /// </summary>
    private static async Task<bool> IsRightAsync(AnalysedMistake mistake, Move move, GameAnalyzer analyzer, CancellationToken cancellationToken)
    {
        var uci = move.ToUci();
        if (mistake.Evaluation.Line is [var best, ..] && best == uci) return true;
        if (mistake.MainLine[mistake.Ply - 1] == uci) return false;

        var after = await analyzer.EvaluateOnceAsync(
            [.. mistake.MainLine.Take(mistake.Ply - 1), uci], mistake.Before.Play(move), mistake.MsPerMove, cancellationToken);
        var mover = mistake.Mover;
        double ForMover(double whitePercent) => mover == Color.White ? whitePercent : 100 - whitePercent;
        var before = ForMover(WinningChances.WhitePercent(new EngineScore(mistake.Evaluation.Cp, mistake.Evaluation.Mate), mover));
        var afterPercent = ForMover(WinningChances.WhitePercent(new EngineScore(after.Cp, after.Mate), mover.Opposite()));
        return Assessor.Loss(before, afterPercent) < Assessor.InaccuracyFrom;
    }

    /// <summary>The Owner's own current mistakes by item key, and the items kept in step with them.</summary>
    private static async Task<(List<TrainingItemRecord> Items, Dictionary<string, AnalysedMistake> Mistakes)> SyncAsync(
        CollectionDbContext db, AnalysedMistakes analysed, TimeProvider clock)
    {
        var mistakes = await OwnersCurrentMistakesAsync(db, analysed);
        var items = await TrainingItems.SyncAsync(db, TrainingKind.Mistake, [.. mistakes.Keys], clock);
        return (items, mistakes);
    }

    /// <summary>
    /// The Owner's analysed mistakes in moves the Game still has: its main line up to the mistake is
    /// still the analysed one (checked against the position index), so the Game opens on the same position.
    /// </summary>
    private static async Task<Dictionary<string, AnalysedMistake>> OwnersCurrentMistakesAsync(CollectionDbContext db, AnalysedMistakes analysed)
    {
        var owners = await db.Games.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.OwnerColour.ToColor());
        var candidates = (await analysed.AllAsync(db))
            .Where(m => owners.TryGetValue(m.GameId, out var owner) && m.Mover == owner)
            .ToList();
        var gameIds = candidates.Select(m => m.GameId).Distinct().ToList();
        var deepest = candidates.Count == 0 ? 0 : candidates.Max(m => m.Ply);
        var current = (await db.PositionIndex.AsNoTracking()
                .Where(p => gameIds.Contains(p.GameId) && p.Ply < deepest)
                .Select(p => new { p.GameId, p.Ply, p.Uci })
                .ToListAsync())
            .ToLookup(p => p.GameId, p => (p.Ply, p.Uci));

        var mistakes = new Dictionary<string, AnalysedMistake>();
        foreach (var mistake in candidates)
        {
            var played = current[mistake.GameId].Where(p => p.Ply < mistake.Ply).OrderBy(p => p.Ply).Select(p => p.Uci);
            if (played.SequenceEqual(mistake.MainLine.Take(mistake.Ply))) mistakes.TryAdd(mistake.Key, mistake);
        }
        return mistakes;
    }
}
