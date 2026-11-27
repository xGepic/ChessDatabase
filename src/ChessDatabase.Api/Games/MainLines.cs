using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>A Game's main line as the position index keeps it: its moves (UCI) and the key of every position, the start first.</summary>
public sealed record IndexedMainLine(IReadOnlyList<string> Uci, IReadOnlyList<long> Keys);

/// <summary>
/// Reading Games' main lines, many at once. The moves are the source of truth; the position index
/// is the copy <see cref="GameMoves"/> derives from them for searching. Each read takes whichever
/// a question needs: the index when moves and positions are enough (cheap), the moves when it
/// needs their ids or SAN. Also owns the one rule for whether something worked out from a main
/// line, such as an Engine Assessment, still matches the Game.
/// </summary>
public static class MainLines
{
    /// <summary>The main lines from the position index. Every Game asked for is in the answer; one that doesn't exist has an empty line.</summary>
    public static async Task<Dictionary<int, IndexedMainLine>> IndexedAsync(
        CollectionDbContext db, IReadOnlyCollection<int> gameIds, CancellationToken cancellationToken = default)
    {
        var rows = (await db.PositionIndex.AsNoTracking().Where(p => gameIds.Contains(p.GameId))
                .Select(p => new { p.GameId, p.Ply, p.Hash, p.Uci }).ToListAsync(cancellationToken))
            .ToLookup(p => p.GameId);
        return gameIds.Distinct().ToDictionary(id => id, id =>
        {
            var line = rows[id].OrderBy(r => r.Ply).ToList();
            return new IndexedMainLine([.. line.Where(r => r.Uci is not null).Select(r => r.Uci!)], [.. line.Select(r => r.Hash)]);
        });
    }

    /// <summary>The main-line moves themselves (with ids and SAN). Every Game asked for is in the answer; one that doesn't exist has no moves.</summary>
    public static async Task<Dictionary<int, IReadOnlyList<MoveNodeRecord>>> MovesAsync(
        CollectionDbContext db, IReadOnlyCollection<int> gameIds, CancellationToken cancellationToken = default)
    {
        var nodes = (await db.MoveNodes.AsNoTracking().Where(n => gameIds.Contains(n.GameId)).ToListAsync(cancellationToken))
            .ToLookup(n => n.GameId);
        return gameIds.Distinct().ToDictionary(id => id, id => new MoveTree(nodes[id]).MainLine());
    }

    /// <summary>
    /// How many moves from the start a Game's main line still plays as <paramref name="analysed"/>
    /// did: everything worked out from those moves holds for that many. An Engine Assessment matches
    /// the Game when it follows all of them and the Game has no more; a mistake still stands when
    /// the Game follows the moves up to and including it.
    /// </summary>
    public static int FollowedPlies(IReadOnlyList<string> current, IReadOnlyList<string> analysed)
    {
        var plies = 0;
        while (plies < current.Count && plies < analysed.Count && current[plies] == analysed[plies]) plies++;
        return plies;
    }
}
