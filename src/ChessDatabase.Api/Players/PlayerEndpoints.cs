using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Players;

public sealed record PlayerDto(int Id, string Name);

public sealed record CreatePlayerRequest(string? Name);

/// <summary>The details the Owner records for a Player, as entered: trimmed and checked by <see cref="Validate"/>.</summary>
/// <param name="FideId">Digits only; empty means none.</param>
/// <param name="Club">Empty means none.</param>
public sealed record PlayerDetails(string? Name, string? FideId, string? Club)
{
    /// <summary>A 400 problem for invalid details, or null. The name may only be shared with the Players in <paramref name="self"/>.</summary>
    public IResult? Validate(IEnumerable<PlayerRecord> all, params int[] self)
    {
        var name = Name?.Trim() ?? "";
        var fideId = FideId?.Trim() ?? "";
        var club = Club?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (name.Length == 0) errors["name"] = ["A Player needs a name."];
        else if (all.Any(p => !self.Contains(p.Id) && string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            errors["name"] = ["Another Player already has that name. To make them one, merge the two Players."];
        if (fideId.Length > 0 && (fideId.Length > PlayerRecord.MaxFideIdLength || !fideId.All(char.IsAsciiDigit)))
            errors["fideId"] = [$"A FIDE ID is up to {PlayerRecord.MaxFideIdLength} digits."];
        if (club.Length > PlayerRecord.MaxClubLength) errors["club"] = [$"At most {PlayerRecord.MaxClubLength} characters."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    /// <summary>Gives the Player these details; call only after <see cref="Validate"/> passed.</summary>
    public void ApplyTo(PlayerRecord player)
    {
        player.Name = Name!.Trim();
        player.FideId = FideId?.Trim() is { Length: > 0 } f ? f : null;
        player.Club = Club?.Trim() is { Length: > 0 } c ? c : null;
    }
}

/// <param name="FideId">Digits only; empty removes it.</param>
public sealed record UpdatePlayerRequest(string? Name, string? FideId, string? Club)
{
    public PlayerDetails Details => new(Name, FideId, Club);
}

/// <summary>Merge <paramref name="MergeId"/> into the Player in the route, keeping these details (empty FIDE ID or club: none).</summary>
public sealed record MergePlayersRequest(int MergeId, string? Name, string? FideId, string? Club)
{
    public PlayerDetails Details => new(Name, FideId, Club);
}

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
            if (body.Details.Validate(all, id) is { } problem) return problem;

            body.Details.ApplyTo(player);
            await db.SaveChangesAsync();
            return Results.Ok(new PlayerDto(player.Id, player.Name));
        });

        // Two Player records that are really one person: the merged one's Games move to the kept one,
        // which takes the chosen details; the merged one is deleted. All or nothing.
        players.MapPost("/{id:int}/merge", async (int id, MergePlayersRequest body, CollectionDbContext db) =>
        {
            if (body.MergeId == id) return Invalid("mergeId", "Choose another Player to merge with.");
            // Checked inside the transaction, so nothing can change the Players between the check and the merge.
            await using var transaction = await db.Database.BeginTransactionAsync();
            var all = await db.Players.ToListAsync();
            if (all.FirstOrDefault(p => p.Id == id) is not { } kept || all.FirstOrDefault(p => p.Id == body.MergeId) is not { } merged)
                return Results.NotFound();
            if (body.Details.Validate(all, id, merged.Id) is { } problem) return problem;

            await db.Games.Where(g => g.OpponentId == merged.Id).ExecuteUpdateAsync(s => s.SetProperty(g => g.OpponentId, kept.Id));
            db.Players.Remove(merged);
            await db.SaveChangesAsync(); // the merged Player's name is free before the kept one may take it
            body.Details.ApplyTo(kept);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new PlayerDto(kept.Id, kept.Name));
        });
    }
}
