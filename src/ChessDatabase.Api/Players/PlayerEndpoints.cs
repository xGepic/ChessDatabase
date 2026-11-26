using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Players;

public sealed record PlayerDto(int Id, string Name);

public sealed record CreatePlayerRequest(string? Name);

public static class PlayerEndpoints
{
    public static void MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        var players = app.MapGroup("/api/players");

        // Autocomplete. The Collection holds a few hundred Players at most, so matching in memory
        // keeps case-insensitive search correct for names with umlauts, which SQLite's LIKE is not.
        players.MapGet("/", async (string? search, CollectionDbContext db) =>
        {
            var all = await db.Players.AsNoTracking().ToListAsync();
            var term = search?.Trim() ?? "";
            return all
                .Where(p => term.Length == 0 || p.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new PlayerDto(p.Id, p.Name))
                .ToList();
        });

        players.MapPost("/", async (CreatePlayerRequest body, CollectionDbContext db) =>
        {
            var name = body.Name?.Trim() ?? "";
            if (name.Length == 0)
                return Invalid("name", "A Player needs a name.");

            var all = await db.Players.ToListAsync();
            var existing = all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
            if (existing is not null) return Results.Ok(new PlayerDto(existing.Id, existing.Name));

            var player = new PlayerRecord { Name = name };
            db.Players.Add(player);
            await db.SaveChangesAsync();
            return Results.Created($"/api/players/{player.Id}", new PlayerDto(player.Id, player.Name));
        });
    }
}
