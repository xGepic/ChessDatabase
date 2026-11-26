using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>
/// Events and Tags are referred to by name. A name that matches an existing one (ignoring case)
/// links to it, so a tournament's Games group together; a new name creates a record.
/// </summary>
public static class EventsAndTags
{
    /// <summary>At most this many autocomplete suggestions for a search.</summary>
    private const int SuggestionLimit = 20;

    public static async Task<EventRecord?> FindOrCreateEventAsync(CollectionDbContext db, string? name)
    {
        name = GameValues.OptionalText(name);
        if (name is null) return null;
        var existing = (await db.Events.ToListAsync()).Concat(db.Events.Local).FirstOrDefault(e => Names.Same(e.Name, name));
        return existing ?? db.Events.Add(new EventRecord { Name = name }).Entity;
    }

    public static async Task<List<TagRecord>> FindOrCreateTagsAsync(CollectionDbContext db, IEnumerable<string> names)
    {
        var all = await db.Tags.ToListAsync();
        var tags = new List<TagRecord>();
        foreach (var name in names.Select(GameValues.OptionalText).OfType<string>())
        {
            var tag = all.FirstOrDefault(t => Names.Same(t.Name, name));
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
    /// Names containing the search text, ignoring case, sorted. A search returns a few suggestions;
    /// without one, everything is listed (for filter choices).
    /// </summary>
    public static List<T> Search<T>(IEnumerable<T> all, Func<T, string> name, string? search)
    {
        var term = search?.Trim() ?? "";
        return [.. all
            .Where(x => term.Length == 0 || name(x).Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(name, StringComparer.CurrentCultureIgnoreCase)
            .Take(term.Length == 0 ? int.MaxValue : SuggestionLimit)];
    }
}
