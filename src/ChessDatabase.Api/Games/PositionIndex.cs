using ChessDatabase.Api.Collection;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>
/// The position index (<see cref="PositionIndexRecord"/>): how a main line becomes rows, and the
/// one-time filling for Games stored before the index existed. Kept current by <see cref="MainLine"/>.
/// </summary>
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

    /// <summary>
    /// Indexes every Game that has no rows yet: the Games stored before the index existed. Runs at
    /// start-up after the migrations; afterwards every Game keeps its own rows current.
    /// </summary>
    public static void Backfill(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionDbContext>();
        var missing = db.Games.Where(g => !g.Positions.Any()).Select(g => g.Id).ToList();
        if (missing.Count == 0) return;

        var nodesByGame = db.MoveNodes.AsNoTracking().Where(n => missing.Contains(n.GameId)).ToList().ToLookup(n => n.GameId);
        foreach (var id in missing)
            foreach (var row in Rows([.. new MoveTree(nodesByGame[id]).MainLine().Select(n => n.Uci)]))
            {
                row.GameId = id;
                db.PositionIndex.Add(row);
            }
        db.SaveChanges();
    }
}
