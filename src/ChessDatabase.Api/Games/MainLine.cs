using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Openings;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>
/// Everything derived from a Game's main line: the ECO code and opening name, and the position
/// index. Applied when a Game is created or imported and after every change to its move tree, so
/// derived data always follows the main line.
/// </summary>
public static class MainLine
{
    /// <summary>
    /// Sets the derived data on a Game for its main line (UCI moves from the start). The Game's
    /// index rows must be loaded if it has any: they are replaced, all within the next SaveChanges.
    /// </summary>
    public static void Apply(GameRecord game, IReadOnlyList<string> mainLine, OpeningClassifier openings)
    {
        var positions = new List<Position>(mainLine.Count);
        var position = Position.Initial;
        foreach (var uci in mainLine)
        {
            position = position.Play(Move.TryParseUci(uci, out var move) ? move : throw new InvalidDataException($"'{uci}' is not UCI."));
            positions.Add(position);
        }
        var opening = openings.Classify(positions);
        game.Eco = opening?.Eco;
        game.OpeningName = opening?.Name;
        game.Positions = PositionIndex.Rows(mainLine);
    }

    /// <summary>Re-derives a stored Game's data after its move tree changed, and saves.</summary>
    public static async Task RefreshAsync(CollectionDbContext db, OpeningClassifier openings, int gameId)
    {
        var game = await db.Games.Include(g => g.MoveNodes).Include(g => g.Positions).AsSplitQuery().SingleAsync(g => g.Id == gameId);
        Apply(game, [.. new MoveTree(game.MoveNodes).MainLine().Select(n => n.Uci)], openings);
        await db.SaveChangesAsync();
    }
}
