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
/// Mistake Training: every move of the Owner that an Engine Assessment classifies as a mistake or
/// a blunder is an item, the position before it a puzzle: find a better move. Any move that loses
/// less winning chance than an inaccuracy would (compared with the Engine's best) counts as right.
/// Items are keyed by Game and position, so re-running Game Analysis keeps the progress of
/// positions that are still mistakes; positions that no longer are lose their items.
/// </summary>
public static class MistakeTrainingEndpoints
{
    /// <summary>Maps /api/training/mistakes: the summary, /next and /{itemId}/answer.</summary>
    public static void MapMistakeTrainingEndpoints(this IEndpointRouteBuilder app)
    {
        var training = app.MapGroup("/api/training/mistakes");

        training.MapGet("/", async (CollectionDbContext db, TimeProvider clock) =>
            Results.Ok(TrainingItems.Summary((await SyncAsync(db, clock)).Items, clock)));

        // The item due first, or 204 when nothing is due today.
        training.MapGet("/next", async (CollectionDbContext db, TimeProvider clock) =>
        {
            var (items, mistakes) = await SyncAsync(db, clock);
            if (TrainingItems.NextDue(items, clock) is not { } next) return Results.NoContent();
            var m = mistakes[next.Key];
            var game = await db.Games.AsNoTracking().Include(g => g.Opponent).SingleAsync(g => g.Id == m.GameId);
            return Results.Ok(new MistakeItemDto(
                next.Id, m.GameId, m.Ply, m.Side, m.Before.ToFen(), m.Ply > 1 ? m.MainLine[m.Ply - 2] : null, m.PlayedSan,
                m.Classification, game.Opponent.Name, game.Date, next.ReviewedAt is null));
        });

        training.MapPost("/{itemId:int}/answer", async (
            int itemId, TrainingAnswerRequest body, CollectionDbContext db, TimeProvider clock, GameAnalyzer analyzer, CancellationToken cancellationToken) =>
        {
            var (items, mistakes) = await SyncAsync(db, clock);
            if (items.FirstOrDefault(i => i.Id == itemId) is not { } item) return Results.NotFound();
            var m = mistakes[item.Key];
            if (!MoveTreeEndpoints.TryReadMove(m.Before, body.Move, out var move))
                return Invalid("move", $"'{body.Move}' is not a legal move here.");

            bool right;
            try
            {
                right = await IsRightAsync(m, move, analyzer, cancellationToken);
            }
            catch (EngineException e)
            {
                return Results.Problem($"The Engine is needed to judge that move: {e.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            SpacedRepetition.Review(item, right, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            var (bestMoves, bestSan) = m.Before.PlayableLine(m.Evaluation.Line);
            var shown = right || bestMoves.Count == 0 ? move : bestMoves[0];
            return Results.Ok(new MistakeAnswerDto(
                right, bestSan, [.. bestMoves.Select(b => b.ToUci())], shown.ToUci(), m.Before.Play(shown).ToFen(),
                new DateTimeOffset(item.DueAt, TimeSpan.Zero)));
        });
    }

    /// <summary>One of the Owner's analysed mistakes.</summary>
    /// <param name="Ply">The mistake's half-move (1 = White's first).</param>
    /// <param name="MainLine">The analysed main line (UCI).</param>
    /// <param name="Before">The position the Owner went wrong in.</param>
    /// <param name="Evaluation">The Engine's evaluation of <paramref name="Before"/> (from the Owner's side) and best line.</param>
    private sealed record Mistake(
        int GameId, int Ply, OwnerColour Side, IReadOnlyList<string> MainLine, Position Before, string PlayedSan,
        MoveClassification Classification, StoredEvaluation Evaluation);

    /// <summary>
    /// The best move is right; the Game's own move is wrong; any other move is evaluated by the
    /// Engine (like Game Analysis does) and is right when it loses less than an inaccuracy.
    /// </summary>
    private static async Task<bool> IsRightAsync(Mistake m, Move move, GameAnalyzer analyzer, CancellationToken cancellationToken)
    {
        var uci = move.ToUci();
        if (m.Evaluation.Line is [var best, ..] && best == uci) return true;
        if (m.MainLine[m.Ply - 1] == uci) return false;

        var after = await analyzer.EvaluateOnceAsync([.. m.MainLine.Take(m.Ply - 1), uci], cancellationToken);
        var mover = m.Before.SideToMove;
        var opponent = mover == Color.White ? Color.Black : Color.White;
        double ForMover(double whitePercent) => mover == Color.White ? whitePercent : 100 - whitePercent;
        var before = ForMover(WinningChances.WhitePercent(new EngineScore(m.Evaluation.Cp, m.Evaluation.Mate), mover));
        var afterPercent = ForMover(WinningChances.WhitePercent(new EngineScore(after.Cp, after.Mate), opponent));
        return (before - afterPercent) * 2 < Assessor.InaccuracyFrom; // percent → points on Lichess's −100…+100 scale
    }

    /// <summary>The Owner's analysed mistakes and blunders by item key, and the items kept in step with them.</summary>
    private static async Task<(List<TrainingItemRecord> Items, Dictionary<string, Mistake> Mistakes)> SyncAsync(
        CollectionDbContext db, TimeProvider clock)
    {
        var mistakes = await FindMistakesAsync(db);
        var items = await TrainingItems.SyncAsync(db, TrainingKind.Mistake, [.. mistakes.Keys], clock);
        return (items, mistakes);
    }

    /// <summary>
    /// Every Owner's move an Engine Assessment classifies as a mistake or blunder, newest Game first.
    /// The key is the Game and the position (as the position index keys it): the same position after
    /// re-running Game Analysis is the same item.
    /// </summary>
    private static async Task<Dictionary<string, Mistake>> FindMistakesAsync(CollectionDbContext db)
    {
        var owners = await db.Games.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.OwnerColour);
        var records = await db.EngineAssessments.AsNoTracking().OrderByDescending(a => a.GameId).ToListAsync();
        var mistakes = new Dictionary<string, Mistake>();
        foreach (var record in records)
        {
            if (!owners.TryGetValue(record.GameId, out var owner)) continue;
            var assessment = AssessmentQueries.Assess(record);
            var position = Position.Initial;
            for (var i = 0; i < record.MainLine.Count; i++)
            {
                var move = Move.TryParseUci(record.MainLine[i], out var parsed) ? parsed : throw new InvalidDataException(record.MainLine[i]);
                var ownersMove = (position.SideToMove == Color.White) == (owner == OwnerColour.White);
                if (ownersMove && assessment.Moves[i].Classification is MoveClassification.Mistake or MoveClassification.Blunder
                    && $"{record.GameId}:{PositionIndex.Key(position)}" is var key && !mistakes.ContainsKey(key))
                {
                    mistakes[key] = new Mistake(
                        record.GameId, i + 1, owner, record.MainLine, position, position.ToSan(move),
                        assessment.Moves[i].Classification!.Value, record.Positions[i]);
                }
                position = position.Play(move);
            }
        }
        return mistakes;
    }
}
