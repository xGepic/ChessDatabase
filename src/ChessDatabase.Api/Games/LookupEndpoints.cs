using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

public sealed record EventDto(int Id, string Name);

public sealed record TagDto(int Id, string Name);

/// <summary>Autocomplete and filter choices for Events, Sites and Tags from earlier Games.</summary>
public static class LookupEndpoints
{
    public static void MapLookupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (string? search, CollectionDbContext db) =>
            EventsAndTags.Search(await db.Events.AsNoTracking().ToListAsync(), e => e.Name, search)
                .Select(e => new EventDto(e.Id, e.Name)));

        app.MapGet("/api/tags", async (string? search, CollectionDbContext db) =>
            EventsAndTags.Search(await db.Tags.AsNoTracking().ToListAsync(), t => t.Name, search)
                .Select(t => new TagDto(t.Id, t.Name)));

        app.MapGet("/api/sites", async (string? search, CollectionDbContext db) =>
        {
            var sites = await db.Games.Where(g => g.Site != null).Select(g => g.Site!).Distinct().ToListAsync();
            return EventsAndTags.Search(sites.DistinctBy(s => s.ToLowerInvariant()), s => s, search);
        });
    }
}
