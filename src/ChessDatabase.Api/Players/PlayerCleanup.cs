using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Players;

/// <summary>
/// A Player exists only through the Owner's Games against them: once no Game is left with a Player,
/// the Player goes too (with their FIDE ID and club).
/// </summary>
public static class PlayerCleanup
{
    /// <summary>Removes these Players if no Game has them as the Opponent any more.</summary>
    public static Task<int> RemoveIfUnusedAsync(CollectionDbContext db, params int[] playerIds) =>
        db.Players
            .Where(p => playerIds.Contains(p.Id) && !db.Games.Any(g => g.OpponentId == p.Id))
            .ExecuteDeleteAsync();

    /// <summary>
    /// Removes every Player without a Game, e.g. one created for a Game that was never started. Run
    /// at startup only: while the app runs, a Player just created in the New Game form has no Game yet.
    /// </summary>
    public static Task<int> RemoveAllUnusedAsync(CollectionDbContext db) =>
        db.Players.Where(p => !db.Games.Any(g => g.OpponentId == p.Id)).ExecuteDeleteAsync();
}
