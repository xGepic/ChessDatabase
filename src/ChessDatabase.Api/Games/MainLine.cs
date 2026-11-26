using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Openings;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>
/// Everything derived from a Game's main line. Called after every change to the move tree, so
/// derived data (the ECO code and opening name, the position index) always follows the main line.
/// </summary>
public static class MainLine
{
    public static async Task RefreshAsync(CollectionDbContext db, OpeningClassifier openings, int gameId)
    {
        var game = await db.Games.Include(g => g.MoveNodes).SingleAsync(g => g.Id == gameId);
        var tree = new MoveTree(game.MoveNodes);
        var opening = openings.Classify(tree.MainLine().Select(n => tree.PositionAfter(n.Id)));
        game.Eco = opening?.Eco;
        game.OpeningName = opening?.Name;
        await Research.PositionIndex.RebuildAsync(db, gameId, tree);
        await db.SaveChangesAsync();
    }
}
