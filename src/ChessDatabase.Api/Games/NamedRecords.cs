using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>
/// Events and Tags are referred to by name. A name that matches an existing one (ignoring case)
/// links to it, so a tournament's Games group together; a new name creates a record.
/// </summary>
public static class NamedRecords
{
    public static async Task<EventRecord?> FindOrCreateEventAsync(CollectionDbContext db, string? name)
    {
        name = GameEndpoints.Text(name);
        if (name is null) return null;
        var existing = Match(await db.Events.ToListAsync(), e => e.Name, name)
                       ?? Match(db.Events.Local, e => e.Name, name);
        return existing ?? db.Events.Add(new EventRecord { Name = name }).Entity;
    }

    public static async Task<List<TagRecord>> FindOrCreateTagsAsync(CollectionDbContext db, IEnumerable<string> names)
    {
        var all = await db.Tags.ToListAsync();
        var tags = new List<TagRecord>();
        foreach (var name in names.Select(GameEndpoints.Text).OfType<string>())
        {
            var tag = Match(all, t => t.Name, name);
            if (tag is null)
            {
                tag = new TagRecord { Name = name };
                all.Add(tag);
                db.Tags.Add(tag);
            }
            if (!tags.Contains(tag)) tags.Add(tag);
        }
        return tags;
    }

    /// <summary>
    /// Names containing the search text, ignoring case, sorted. A search returns at most <paramref name="limit"/>
    /// suggestions; without one, everything is listed (for filter choices).
    /// </summary>
    public static List<T> Search<T>(IEnumerable<T> all, Func<T, string> name, string? search, int limit = 20)
    {
        var term = search?.Trim() ?? "";
        return all
            .Where(x => term.Length == 0 || name(x).Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(name, StringComparer.CurrentCultureIgnoreCase)
            .Take(term.Length == 0 ? int.MaxValue : limit)
            .ToList();
    }

    private static T? Match<T>(IEnumerable<T> all, Func<T, string> name, string wanted) where T : class =>
        all.FirstOrDefault(x => string.Equals(name(x), wanted, StringComparison.CurrentCultureIgnoreCase));
}
