using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Research;

/// <summary>Builds and maintains the position index (<see cref="PositionIndexRecord"/>).</summary>
public static class PositionIndex
{
    /// <summary>A position as the index stores it.</summary>
    public static long Key(Position position) => unchecked((long)position.Hash);

    /// <summary>The index rows for a main line (UCI moves from the starting position).</summary>
    public static List<PositionIndexRecord> Rows(IReadOnlyList<string> mainLine)
    {
        var rows = new List<PositionIndexRecord>(mainLine.Count + 1);
        var position = Position.Initial;
        for (var ply = 0; ; ply++)
        {
            var uci = ply < mainLine.Count ? mainLine[ply] : null;
            rows.Add(new PositionIndexRecord { Ply = ply, Hash = Key(position), Uci = uci });
            if (uci is null) return rows;
            position = position.Play(Move.TryParseUci(uci, out var move) ? move : throw new InvalidDataException($"'{uci}' is not UCI."));
        }
    }

    /// <summary>Replaces a Game's rows with those of its current main line. The new rows are saved with the next SaveChanges.</summary>
    public static async Task RebuildAsync(CollectionDbContext db, int gameId, MoveTree tree)
    {
        await db.PositionIndex.Where(p => p.GameId == gameId).ExecuteDeleteAsync();
        db.PositionIndex.AddRange(ForGame(gameId, [.. tree.MainLine().Select(n => n.Uci)]));
    }

    private static IEnumerable<PositionIndexRecord> ForGame(int gameId, IReadOnlyList<string> mainLine) =>
        Rows(mainLine).Select(r =>
        {
            r.GameId = gameId;
            return r;
        });

    /// <summary>
    /// Indexes every Game that has no rows yet: the Games that existed before the index did. Runs
    /// once at start-up after the migrations; afterwards every Game keeps its own rows current.
    /// </summary>
    public static void Backfill(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
        var missing = db.Games.Where(g => !g.Positions.Any()).Select(g => g.Id).ToList();
        foreach (var id in missing)
        {
            var nodes = db.MoveNodes.AsNoTracking().Where(n => n.GameId == id).ToList();
            db.PositionIndex.AddRange(ForGame(id, [.. new MoveTree(nodes).MainLine().Select(n => n.Uci)]));
        }
        db.SaveChanges();
    }
}
