using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Training;

/// <param name="Due">Items due today (until local midnight).</param>
/// <param name="Total">Every item, due or not.</param>
public sealed record TrainingSummaryDto(int Due, int Total);

/// <summary>
/// The training items of one kind, kept in step with what there is to train (the Repertoire, the
/// Owner's analysed mistakes): new things become new items, due at once; items for things that are
/// gone are removed; the rest keep their spaced-repetition progress.
/// </summary>
public static class TrainingItems
{
    /// <summary>One request at a time brings items in step, so two can't add the same item.</summary>
    private static readonly SemaphoreSlim SyncGate = new(1, 1);

    /// <param name="wanted">The keys there should be items for, in the order new items are to come up.</param>
    /// <returns>The items (tracked), matching <paramref name="wanted"/>.</returns>
    public static async Task<List<TrainingItemRecord>> SyncAsync(
        CollectionDbContext db, TrainingKind kind, IReadOnlyList<string> wanted, TimeProvider clock)
    {
        await SyncGate.WaitAsync();
        try
        {
            var items = await db.TrainingItems.Where(i => i.Kind == kind).ToListAsync();
            var wantedSet = wanted.ToHashSet();
            var gone = items.Where(i => !wantedSet.Contains(i.Key)).ToList();
            var have = items.Select(i => i.Key).ToHashSet();
            var now = clock.GetUtcNow().UtcDateTime;
            var added = wanted.Distinct().Where(k => !have.Contains(k))
                .Select(k => new TrainingItemRecord { Kind = kind, Key = k, Ease = SpacedRepetition.StartEase, DueAt = now })
                .ToList();
            if (gone.Count > 0 || added.Count > 0)
            {
                db.TrainingItems.RemoveRange(gone);
                db.TrainingItems.AddRange(added);
                await db.SaveChangesAsync();
            }
            return [.. items.Except(gone), .. added];
        }
        finally
        {
            SyncGate.Release();
        }
    }

    public static TrainingSummaryDto Summary(IReadOnlyCollection<TrainingItemRecord> items, TimeProvider clock)
    {
        var endOfToday = SpacedRepetition.EndOfToday(clock);
        return new TrainingSummaryDto(items.Count(i => i.DueAt < endOfToday), items.Count);
    }

    /// <summary>The item due first today (a missed one after those already waiting), or null.</summary>
    public static TrainingItemRecord? NextDue(IEnumerable<TrainingItemRecord> items, TimeProvider clock)
    {
        var endOfToday = SpacedRepetition.EndOfToday(clock);
        return items.Where(i => i.DueAt < endOfToday).OrderBy(i => i.DueAt).ThenBy(i => i.Id).FirstOrDefault();
    }
}
