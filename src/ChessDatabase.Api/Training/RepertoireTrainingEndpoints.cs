using System.Globalization;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Training;

/// <param name="Due">Items due today (until local midnight).</param>
/// <param name="Total">Every item, due or not.</param>
public sealed record TrainingSummaryDto(int Due, int Total);

/// <summary>A Repertoire position to answer.</summary>
/// <param name="Fen">The position: the Owner is to move.</param>
/// <param name="Line">How the Repertoire reaches it from the start; its last move is the Opponent's.</param>
/// <param name="New">Never answered before.</param>
public sealed record RepertoireTrainingItemDto(int ItemId, OwnerColour Side, string Fen, IReadOnlyList<BookMove> Line, bool New);

public sealed record TrainingAnswerRequest(string? Move);

/// <param name="Expected">The Repertoire's moves here (SAN, main continuation first); any of them is right.</param>
/// <param name="ExpectedUci">The same moves as UCI, to show on the board.</param>
/// <param name="DueAt">When the item comes back.</param>
public sealed record TrainingAnswerDto(bool Correct, IReadOnlyList<string> Expected, IReadOnlyList<string> ExpectedUci, DateTimeOffset DueAt);

/// <summary>
/// Repertoire Training: every Repertoire position where it is the Owner's move (and the Repertoire
/// has a move) is a training item, kept in step with the Repertoire whenever training is asked
/// for: new lines become new items, removed ones go. Items are scheduled by
/// <see cref="SpacedRepetition"/>.
/// </summary>
public static class RepertoireTrainingEndpoints
{
    /// <summary>One request at a time brings the items in step, so two can't add the same item.</summary>
    private static readonly SemaphoreSlim SyncGate = new(1, 1);

    /// <summary>Maps /api/training/repertoire: the summary, /next and /{itemId}/answer.</summary>
    public static void MapRepertoireTrainingEndpoints(this IEndpointRouteBuilder app)
    {
        var training = app.MapGroup("/api/training/repertoire");

        training.MapGet("/", async (CollectionDbContext db, TimeProvider clock) =>
        {
            var (items, _) = await SyncAsync(db, clock);
            var endOfToday = SpacedRepetition.EndOfToday(clock);
            return Results.Ok(new TrainingSummaryDto(items.Count(i => i.DueAt < endOfToday), items.Count));
        });

        // The item due first, or 204 when nothing is due today.
        training.MapGet("/next", async (CollectionDbContext db, TimeProvider clock) =>
        {
            var (items, books) = await SyncAsync(db, clock);
            var endOfToday = SpacedRepetition.EndOfToday(clock);
            var next = items.Where(i => i.DueAt < endOfToday).OrderBy(i => i.DueAt).ThenBy(i => i.Id).FirstOrDefault();
            if (next is null) return Results.NoContent();
            var (side, position) = Locate(books, next)!.Value;
            return Results.Ok(new RepertoireTrainingItemDto(next.Id, side, position.Position.ToFen(), position.Line, next.ReviewedAt is null));
        });

        training.MapPost("/{itemId:int}/answer", async (int itemId, TrainingAnswerRequest body, CollectionDbContext db, TimeProvider clock) =>
        {
            var (items, books) = await SyncAsync(db, clock);
            if (items.FirstOrDefault(i => i.Id == itemId) is not { } item || Locate(books, item) is not { } found) return Results.NotFound();
            var position = found.Position;
            if (!MoveTreeEndpoints.TryReadMove(position.Position, body.Move, out var move))
                return Invalid("move", $"'{body.Move}' is not a legal move here.");

            var right = position.Moves.Any(m => m.Uci == move.ToUci());
            SpacedRepetition.Review(item, right, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync();
            return Results.Ok(new TrainingAnswerDto(
                right, [.. position.Moves.Select(m => m.San)], [.. position.Moves.Select(m => m.Uci)],
                new DateTimeOffset(item.DueAt, TimeSpan.Zero)));
        });
    }

    /// <summary>"white:1234": the side and the position (as the position index keys it).</summary>
    private static string KeyOf(OwnerColour side, long position) =>
        $"{side.ToString().ToLowerInvariant()}:{position.ToString(CultureInfo.InvariantCulture)}";

    private static (OwnerColour Side, BookPosition Position)? Locate(Dictionary<OwnerColour, RepertoireBook> books, TrainingItemRecord item)
    {
        var parts = item.Key.Split(':');
        if (parts.Length != 2 || !Enum.TryParse<OwnerColour>(parts[0], ignoreCase: true, out var side)
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var key))
            return null;
        return books[side].Find(key) is { } position ? (side, position) : null;
    }

    /// <summary>
    /// Brings the items in step with both Repertoires and returns them (tracked) with the books.
    /// New items are added shortest line first, so a new Repertoire is trained from the opening on.
    /// </summary>
    private static async Task<(List<TrainingItemRecord> Items, Dictionary<OwnerColour, RepertoireBook> Books)> SyncAsync(
        CollectionDbContext db, TimeProvider clock)
    {
        await SyncGate.WaitAsync();
        try
        {
            return await SyncUnguardedAsync(db, clock);
        }
        finally
        {
            SyncGate.Release();
        }
    }

    private static async Task<(List<TrainingItemRecord> Items, Dictionary<OwnerColour, RepertoireBook> Books)> SyncUnguardedAsync(
        CollectionDbContext db, TimeProvider clock)
    {
        var books = new Dictionary<OwnerColour, RepertoireBook>
        {
            [OwnerColour.White] = await RepertoireBook.LoadAsync(db, OwnerColour.White),
            [OwnerColour.Black] = await RepertoireBook.LoadAsync(db, OwnerColour.Black),
        };
        var wanted = books
            .SelectMany(b => b.Value.Positions
                .Where(p => p.Moves.Count > 0 && p.Position.SideToMove == (b.Key == OwnerColour.White ? Rules.Color.White : Rules.Color.Black))
                .Select(p => (Key: KeyOf(b.Key, p.Key), p.Line.Count)))
            .OrderBy(p => p.Count)
            .Select(p => p.Key)
            .ToList();

        var items = await db.TrainingItems.Where(i => i.Kind == TrainingKind.Repertoire).ToListAsync();
        var wantedSet = wanted.ToHashSet();
        var gone = items.Where(i => !wantedSet.Contains(i.Key)).ToList();
        var have = items.Select(i => i.Key).ToHashSet();
        var now = clock.GetUtcNow().UtcDateTime;
        var added = wanted.Where(k => !have.Contains(k))
            .Select(k => new TrainingItemRecord { Kind = TrainingKind.Repertoire, Key = k, Ease = SpacedRepetition.StartEase, DueAt = now })
            .ToList();
        if (gone.Count > 0 || added.Count > 0)
        {
            db.TrainingItems.RemoveRange(gone);
            db.TrainingItems.AddRange(added);
            await db.SaveChangesAsync();
        }
        return ([.. items.Except(gone), .. added], books);
    }
}
