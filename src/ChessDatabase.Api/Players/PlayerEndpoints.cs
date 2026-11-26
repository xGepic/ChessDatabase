using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Players;

public sealed record PlayerDto(int Id, string Name);

public sealed record CreatePlayerRequest(string? Name);

/// <param name="FideId">Digits only; empty removes it.</param>
public sealed record UpdatePlayerRequest(string? Name, string? FideId, string? Club);

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

        players.MapPut("/{id:int}", async (int id, UpdatePlayerRequest body, CollectionDbContext db) =>
        {
            var all = await db.Players.ToListAsync();
            if (all.FirstOrDefault(p => p.Id == id) is not { } player) return Results.NotFound();

            var name = body.Name?.Trim() ?? "";
            var fideId = body.FideId?.Trim() ?? "";
            var club = body.Club?.Trim() ?? "";
            var errors = new Dictionary<string, string[]>();
            if (name.Length == 0) errors["name"] = ["A Player needs a name."];
            else if (all.Any(p => p.Id != id && string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                errors["name"] = ["Another Player already has that name. To make them one, merge the two Players."];
            if (fideId.Length > 0 && (fideId.Length > PlayerRecord.MaxFideIdLength || !fideId.All(char.IsAsciiDigit)))
                errors["fideId"] = [$"A FIDE ID is up to {PlayerRecord.MaxFideIdLength} digits."];
            if (club.Length > PlayerRecord.MaxClubLength) errors["club"] = [$"At most {PlayerRecord.MaxClubLength} characters."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            player.Name = name;
            player.FideId = fideId.Length > 0 ? fideId : null;
            player.Club = club.Length > 0 ? club : null;
            await db.SaveChangesAsync();
            return Results.Ok(new PlayerDto(player.Id, player.Name));
        });
    }
}
