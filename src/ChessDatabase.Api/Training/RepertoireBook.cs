using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Training;

/// <summary>A move the Repertoire has from a position.</summary>
public sealed record BookMove(string Uci, string San);

/// <summary>
/// One position of a Repertoire, however its lines reach it.
/// </summary>
/// <param name="Line">The shortest line in the Repertoire that reaches the position (the first in reading order among equals).</param>
/// <param name="Moves">What the Repertoire plays from here: the main continuation first; empty where the Repertoire ends.</param>
public sealed record BookPosition(long Key, Position Position, IReadOnlyList<BookMove> Line, IReadOnlyList<BookMove> Moves);

/// <summary>
/// A Repertoire seen by position rather than as a tree: the same position reached by different
/// move orders is one entry, with every move the Repertoire has from it. Deviation checks and
/// Repertoire Training both work this way, so transpositions count as in the Repertoire.
/// </summary>
public sealed class RepertoireBook
{
    private readonly Dictionary<long, BookPosition> _positions;

    private RepertoireBook(Dictionary<long, BookPosition> positions) => _positions = positions;

    public static async Task<RepertoireBook> LoadAsync(CollectionDbContext db, OwnerColour side) =>
        From(new RepertoireTree(side, await db.RepertoireNodes.AsNoTracking().Where(n => n.Side == side).ToListAsync()));

    /// <summary>Walks the tree breadth first, so each position keeps its shortest line.</summary>
    public static RepertoireBook From(RepertoireTree tree)
    {
        var lines = new Dictionary<long, (Position Position, List<BookMove> Line, List<BookMove> Moves)>();
        var queue = new Queue<(int? NodeId, List<BookMove> Line)>([(null, [])]);
        while (queue.TryDequeue(out var item))
        {
            var position = tree.PositionAfter(item.NodeId);
            var key = PositionIndex.Key(position);
            if (!lines.TryGetValue(key, out var entry)) lines[key] = entry = (position, item.Line, []);
            foreach (var child in tree.ChildrenOf(item.NodeId))
            {
                if (!entry.Moves.Any(m => m.Uci == child.Uci)) entry.Moves.Add(new BookMove(child.Uci, child.San));
                queue.Enqueue((child.Id, [.. item.Line, new BookMove(child.Uci, child.San)]));
            }
        }
        return new RepertoireBook(lines.ToDictionary(e => e.Key, e => new BookPosition(e.Key, e.Value.Position, e.Value.Line, e.Value.Moves)));
    }

    /// <summary>Every position of the Repertoire, the starting position included.</summary>
    public IEnumerable<BookPosition> Positions => _positions.Values;

    /// <summary>Whether the Repertoire reaches the position (by any move order).</summary>
    public bool Contains(long key) => _positions.ContainsKey(key);

    /// <summary>The Repertoire's moves from a position; empty where it ends or never goes.</summary>
    public IReadOnlyList<BookMove> MovesFrom(long key) => _positions.TryGetValue(key, out var p) ? p.Moves : [];

    public BookPosition? Find(long key) => _positions.GetValueOrDefault(key);
}
