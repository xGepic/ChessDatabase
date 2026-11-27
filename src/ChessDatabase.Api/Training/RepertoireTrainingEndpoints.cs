using System.Globalization;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Training;

/// <summary>A Repertoire position to answer.</summary>
/// <param name="Fen">The position: the Owner is to move.</param>
/// <param name="FenBefore">The position before the Opponent's last move, so the UI can show it being played; null at the start.</param>
/// <param name="Line">How the Repertoire reaches it from the start; its last move is the Opponent's.</param>
/// <param name="New">Never answered before.</param>
public sealed record RepertoireTrainingItemDto(int ItemId, OwnerColour Side, string Fen, string? FenBefore, IReadOnlyList<RepertoireMove> Line, bool New);

public sealed record TrainingAnswerRequest(string? Move);

/// <param name="Expected">The Repertoire's moves here (SAN, main continuation first); any of them is right.</param>
/// <param name="ExpectedUci">The same moves as UCI.</param>
/// <param name="Shown">The move to show on the board: the Owner's when right, the Repertoire's main move when wrong.</param>
/// <param name="FenAfter">The position after <paramref name="Shown"/>.</param>
/// <param name="DueAt">When the item comes back.</param>
public sealed record TrainingAnswerDto(
    bool Correct, IReadOnlyList<string> Expected, IReadOnlyList<string> ExpectedUci, string Shown, string FenAfter, DateTimeOffset DueAt);

/// <summary>What a Repertoire training item drills: a side's Repertoire position (as the position index keys it).</summary>
public readonly record struct RepertoireItemKey(OwnerColour Side, long Position)
{
    /// <summary>"white:1234", as <see cref="TrainingItemRecord.Key"/> stores it.</summary>
    public override string ToString() =>
        $"{Side.ToString().ToLowerInvariant()}:{Position.ToString(CultureInfo.InvariantCulture)}";

    public static RepertoireItemKey? Parse(string key)
    {
        var parts = key.Split(':');
        return parts.Length == 2 && Enum.TryParse<OwnerColour>(parts[0], ignoreCase: true, out var side)
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
            ? new RepertoireItemKey(side, position)
            : null;
    }
}

/// <summary>
/// Repertoire Training: every Repertoire position where it is the Owner's move (and the Repertoire
/// has a move) is a training item, kept in step with the Repertoire whenever training is asked
/// for: new lines become new items, removed ones go. Items are scheduled by
/// <see cref="SpacedRepetition"/>.
/// </summary>
public static class RepertoireTrainingEndpoints
{
    /// <summary>Maps /api/training/repertoire: the summary, /next and /{itemId}/answer.</summary>
    public static void MapRepertoireTrainingEndpoints(this IEndpointRouteBuilder app)
    {
        var training = app.MapGroup("/api/training/repertoire");

        training.MapGet("/", async (CollectionDbContext db, TimeProvider clock) =>
        {
            var (items, _) = await SyncAsync(db, clock);
            return Results.Ok(TrainingItems.Summary(items, clock));
        });

        // The item due first, or 204 when nothing is due today.
        training.MapGet("/next", async (CollectionDbContext db, TimeProvider clock) =>
        {
            var (items, repertoires) = await SyncAsync(db, clock);
            if (TrainingItems.NextDue(items, clock) is not { } next) return Results.NoContent();
            var (side, position) = Locate(repertoires, next)!.Value;
            return Results.Ok(new RepertoireTrainingItemDto(
                next.Id, side, position.Position.ToFen(), BeforeLastMove(position.Line)?.ToFen(), position.Line, next.ReviewedAt is null));
        });

        training.MapPost("/{itemId:int}/answer", async (int itemId, TrainingAnswerRequest body, CollectionDbContext db, TimeProvider clock) =>
        {
            var (items, repertoires) = await SyncAsync(db, clock);
            if (items.FirstOrDefault(i => i.Id == itemId) is not { } item || Locate(repertoires, item) is not { } found) return Results.NotFound();
            var position = found.Position;
            if (!position.Position.TryReadMove(body.Move, out var move))
                return Invalid("move", $"'{body.Move}' is not a legal move here.");

            var right = position.Moves.Any(m => m.Uci == move.ToUci());
            SpacedRepetition.Review(item, right, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync();
            var shown = right ? move : Move.ParseUci(position.Moves[0].Uci);
            return Results.Ok(new TrainingAnswerDto(
                right, [.. position.Moves.Select(m => m.San)], [.. position.Moves.Select(m => m.Uci)],
                shown.ToUci(), position.Position.Play(shown).ToFen(), new DateTimeOffset(item.DueAt, TimeSpan.Zero)));
        });
    }

    private static (OwnerColour Side, RepertoirePosition Position)? Locate(
        Dictionary<OwnerColour, RepertoireByPosition> repertoires, TrainingItemRecord item) =>
        RepertoireItemKey.Parse(item.Key) is { } key && repertoires[key.Side].Find(key.Position) is { } position
            ? (key.Side, position)
            : null;

    /// <summary>The position before a line's last move; null for the empty line.</summary>
    private static Position? BeforeLastMove(IReadOnlyList<RepertoireMove> line)
    {
        if (line.Count == 0) return null;
        return Line.FromUci(line.Select(m => m.Uci)).Positions[^2];
    }

    /// <summary>
    /// Brings the items in step with both Repertoires and returns them (tracked) with the Repertoires.
    /// New items come up shortest line first, so a new Repertoire is trained from the opening on.
    /// </summary>
    private static async Task<(List<TrainingItemRecord> Items, Dictionary<OwnerColour, RepertoireByPosition> Repertoires)> SyncAsync(
        CollectionDbContext db, TimeProvider clock)
    {
        var repertoires = await RepertoireByPosition.LoadBothAsync(db);
        var wanted = repertoires
            .SelectMany(r => r.Value.Positions
                .Where(p => p.Moves.Count > 0 && p.Position.SideToMove == r.Key.ToColor())
                .Select(p => (Key: new RepertoireItemKey(r.Key, p.Key).ToString(), p.Line.Count)))
            .OrderBy(p => p.Count)
            .Select(p => p.Key)
            .ToList();
        return (await TrainingItems.SyncAsync(db, TrainingKind.Repertoire, wanted, clock), repertoires);
    }
}
