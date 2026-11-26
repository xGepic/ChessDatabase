using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Training;

public enum DeviationStatus
{
    /// <summary>A side played a move that leaves the Repertoire.</summary>
    Deviation,

    /// <summary>The Repertoire has no move for a position the Game reached, before anyone left it.</summary>
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
/// <param name="Ply">The half-move that deviated, or for the end of the Repertoire the first one it has no move for; null when the Game ended in book.</param>
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
/// colour, by position (any move order), so transpositions back into the Repertoire count as
/// in-book: the Deviation is the move that leaves it for good.
/// </summary>
public static class DeviationEndpoints
{
    /// <summary>Maps GET /api/games/{id}/deviation and GET /api/repertoire/deviations (the game list's filters).</summary>
    public static void MapDeviationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/games/{id:int}/deviation", async (int id, CollectionDbContext db) =>
        {
            var game = await db.Games.AsNoTracking().Include(g => g.Positions).SingleOrDefaultAsync(g => g.Id == id);
            if (game is null) return Results.NotFound();
            var book = await RepertoireBook.LoadAsync(db, game.OwnerColour);
            var found = Find(book, game.OwnerColour, game.Positions);
            if (found.Ply is not { } ply) return Results.Ok(new DeviationDto(found.Status, null, null, null, null, []));

            var node = (await MainLineAsync(db, id))[ply - 1];
            return Results.Ok(new DeviationDto(found.Status, ply, node.Id, found.By, node.San, found.Expected));
        });

        // The Owner's own Deviations: the lines the Owner keeps forgetting. Newest first.
        app.MapGet("/api/repertoire/deviations", async ([AsParameters] GameListQuery query, CollectionDbContext db) =>
        {
            if (query.Validate() is { Count: > 0 } errors) return Results.ValidationProblem(errors);
            var rows = await query.RunAsync(db);
            var ids = rows.Select(r => r.Game.Id).ToList();
            var positions = (await db.PositionIndex.AsNoTracking().Where(p => ids.Contains(p.GameId)).ToListAsync()).ToLookup(p => p.GameId);
            var books = new Dictionary<OwnerColour, RepertoireBook>
            {
                [OwnerColour.White] = await RepertoireBook.LoadAsync(db, OwnerColour.White),
                [OwnerColour.Black] = await RepertoireBook.LoadAsync(db, OwnerColour.Black),
            };

            var list = new List<OwnerDeviationDto>();
            foreach (var (game, summary) in rows)
            {
                var found = Find(books[game.OwnerColour], game.OwnerColour, positions[game.Id]);
                if (found is not { Status: DeviationStatus.Deviation, By: DeviationSide.Owner, Ply: { } ply }) continue;
                var node = (await MainLineAsync(db, game.Id))[ply - 1];
                list.Add(new OwnerDeviationDto(summary, ply, node.Id, node.San, found.Expected));
            }
            return Results.Ok(list);
        });
    }

    private sealed record Found(DeviationStatus Status, int? Ply, DeviationSide? By, IReadOnlyList<string> Expected);

    /// <summary>
    /// Looks for the last main-line position that is in the Repertoire: whatever happened before
    /// it was a transposition that came back. From there the Game either ended, reached the end of
    /// the Repertoire, or left it for good with the next move.
    /// </summary>
    private static Found Find(RepertoireBook book, OwnerColour ownerColour, IEnumerable<PositionIndexRecord> positions)
    {
        var rows = positions.OrderBy(p => p.Ply).ToList();
        var last = rows.FindLastIndex(r => book.Contains(r.Hash));
        if (last < 0 || rows[last].Uci is null) return new Found(DeviationStatus.GameEnded, null, null, []);

        var ply = last + 1;
        var moves = book.MovesFrom(rows[last].Hash);
        if (moves.Count == 0) return new Found(DeviationStatus.EndOfRepertoire, ply, null, []);
        var whiteMoved = ply % 2 == 1;
        var ownerMoved = whiteMoved == (ownerColour == OwnerColour.White);
        return new Found(DeviationStatus.Deviation, ply, ownerMoved ? DeviationSide.Owner : DeviationSide.Opponent, [.. moves.Select(m => m.San)]);
    }

    private static async Task<IReadOnlyList<MoveNodeRecord>> MainLineAsync(CollectionDbContext db, int gameId) =>
        new MoveTree(await db.MoveNodes.AsNoTracking().Where(n => n.GameId == gameId).ToListAsync()).MainLine();
}
