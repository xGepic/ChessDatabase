using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Training;

public enum DeviationStatus
{
    /// <summary>A side played a move that leaves the Repertoire for good.</summary>
    Deviation,

    /// <summary>The Repertoire has no move for a position the Game reached.</summary>
    EndOfRepertoire,

    /// <summary>The Game ended while still in the Repertoire.</summary>
    GameEnded,
}

public enum DeviationSide
{
    Owner,
    Opponent,
}

/// <summary>Where a Game left the Owner's Repertoire for the colour the Owner had.</summary>
/// <param name="Ply">The half-move that deviated, or for the end of the Repertoire the first one it has no move for; null when the Game ended in the Repertoire.</param>
/// <param name="NodeId">The main-line move at <paramref name="Ply"/>.</param>
/// <param name="By">Who deviated; null unless the status is a Deviation.</param>
/// <param name="San">The move played at <paramref name="Ply"/>.</param>
/// <param name="Expected">For a Deviation, what the Repertoire plays there (main continuation first).</param>
public sealed record DeviationDto(
    DeviationStatus Status, int? Ply, int? NodeId, DeviationSide? By, string? San, IReadOnlyList<string> Expected);

/// <summary>A Game where the Owner left their own Repertoire.</summary>
public sealed record OwnerDeviationDto(GameSummaryDto Game, int Ply, int NodeId, string San, IReadOnlyList<string> Expected);

/// <summary>
/// The Deviation check: follows a Game's main line through the Owner's Repertoire for the Owner's
/// colour, by position (any move order). Transpositions back into the Repertoire count as in the
/// Repertoire, so the Deviation is the move that leaves it for good: the move after the last
/// main-line position the Repertoire has.
/// </summary>
public static class DeviationEndpoints
{
    /// <summary>Maps GET /api/games/{id}/deviation and GET /api/repertoire/deviations (the game list's filters).</summary>
    public static void MapDeviationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/games/{id:int}/deviation", async (int id, CollectionDbContext db) =>
        {
            var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id);
            if (game is null) return Results.NotFound();
            var repertoire = await RepertoireByPosition.LoadAsync(db, game.OwnerColour);
            var found = Find(repertoire, game.OwnerColour, (await MainLines.IndexedAsync(db, [id]))[id]);
            if (found.Ply is not { } ply) return Results.Ok(new DeviationDto(found.Status, null, null, null, null, []));

            var node = (await MainLines.MovesAsync(db, [id]))[id][ply - 1];
            return Results.Ok(new DeviationDto(found.Status, ply, node.Id, found.By, node.San, found.Expected));
        });

        // The Owner's own Deviations: the lines the Owner keeps forgetting. Newest first, like the game list.
        app.MapGet("/api/repertoire/deviations", async ([AsParameters] GameListQuery query, CollectionDbContext db) =>
        {
            if (query.Validate() is { Count: > 0 } errors) return Results.ValidationProblem(errors);
            var rows = await query.RunAsync(db);
            var ids = rows.Select(r => r.Game.Id).ToList();
            var mainLines = await MainLines.IndexedAsync(db, ids);
            var repertoires = await RepertoireByPosition.LoadBothAsync(db);

            var deviations = new List<(GameSummaryDto Summary, int Ply, IReadOnlyList<string> Expected)>();
            foreach (var (game, summary) in rows)
            {
                var found = Find(repertoires[game.OwnerColour], game.OwnerColour, mainLines[game.Id]);
                if (found is { Status: DeviationStatus.Deviation, By: DeviationSide.Owner, Ply: { } ply })
                    deviations.Add((summary, ply, found.Expected));
            }

            var moves = await MainLines.MovesAsync(db, [.. deviations.Select(d => d.Summary.Id)]);
            return Results.Ok(deviations.Select(d =>
            {
                var node = moves[d.Summary.Id][d.Ply - 1];
                return new OwnerDeviationDto(d.Summary, d.Ply, node.Id, node.San, d.Expected);
            }).ToList());
        });
    }

    private sealed record Found(DeviationStatus Status, int? Ply, DeviationSide? By, IReadOnlyList<string> Expected);

    /// <summary>
    /// Looks for the last main-line position the Repertoire has (the starting position always is):
    /// whatever happened before it was a transposition that came back. From there the Game either
    /// ended, reached the end of the Repertoire, or left it for good with the next move.
    /// </summary>
    private static Found Find(RepertoireByPosition repertoire, OwnerColour ownerColour, IndexedMainLine mainLine)
    {
        var last = mainLine.PositionKeys.ToList().FindLastIndex(repertoire.Contains);
        if (last < 0 || last == mainLine.Uci.Count) return new Found(DeviationStatus.GameEnded, null, null, []);

        var ply = last + 1;
        var moves = repertoire.MovesFrom(mainLine.PositionKeys[last]);
        if (moves.Count == 0) return new Found(DeviationStatus.EndOfRepertoire, ply, null, []);
        var whiteMoved = ply % 2 == 1;
        var ownerMoved = whiteMoved == (ownerColour == OwnerColour.White);
        return new Found(DeviationStatus.Deviation, ply, ownerMoved ? DeviationSide.Owner : DeviationSide.Opponent, [.. moves.Select(m => m.San)]);
    }
}
