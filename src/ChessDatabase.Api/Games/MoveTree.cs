using ChessDatabase.Api.Collection;
using ChessDatabase.Rules;

namespace ChessDatabase.Api.Games;

/// <summary>
/// A Game's move tree, loaded from its stored nodes. Positions are replayed through Chess Rules,
/// so every node's position comes from the rules, never from stored text.
/// </summary>
public sealed class MoveTree
{
    private readonly Dictionary<int, MoveNodeRecord> _nodes;
    private readonly Dictionary<int, List<MoveNodeRecord>> _children = [];
    private readonly List<MoveNodeRecord> _firstMoves;
    private readonly Dictionary<int, (Position Position, int Ply)> _after = [];

    public MoveTree(IEnumerable<MoveNodeRecord> nodes)
    {
        _nodes = nodes.ToDictionary(n => n.Id);
        foreach (var group in _nodes.Values.Where(n => n.ParentId is not null).GroupBy(n => n.ParentId!.Value))
            _children[group.Key] = [.. group.OrderBy(n => n.Order)];
        _firstMoves = [.. _nodes.Values.Where(n => n.ParentId is null).OrderBy(n => n.Order)];

        var queue = new Queue<(MoveNodeRecord Node, Position Before, int Ply)>(
            _firstMoves.Select(n => (n, StartPosition, 1)));
        while (queue.TryDequeue(out var item))
        {
            var after = item.Before.Play(ParseStoredMove(item.Node.Uci));
            _after[item.Node.Id] = (after, item.Ply);
            foreach (var child in ChildrenOf(item.Node.Id)) queue.Enqueue((child, after, item.Ply + 1));
        }
    }

    public static Position StartPosition => Position.Initial;

    public bool Contains(int nodeId) => _nodes.ContainsKey(nodeId);

    public MoveNodeRecord this[int nodeId] => _nodes[nodeId];

    /// <summary>The ordered continuations of a node; null means the starting position.</summary>
    public IReadOnlyList<MoveNodeRecord> ChildrenOf(int? nodeId) =>
        nodeId is null ? _firstMoves : _children.TryGetValue(nodeId.Value, out var list) ? list : [];

    /// <summary>The position after a node; null means the starting position.</summary>
    public Position PositionAfter(int? nodeId) => nodeId is null ? StartPosition : _after[nodeId.Value].Position;

    public int PlyOf(int? nodeId) => nodeId is null ? 0 : _after[nodeId.Value].Ply;

    public (IReadOnlyList<int> FirstMoves, IReadOnlyList<MoveNodeDto> Nodes) ToDtos()
    {
        var dtos = new List<MoveNodeDto>(_nodes.Count);
        var stack = new Stack<MoveNodeRecord>(_firstMoves.AsEnumerable().Reverse());
        while (stack.TryPop(out var node))
        {
            var children = ChildrenOf(node.Id);
            var (after, ply) = _after[node.Id];
            dtos.Add(new MoveNodeDto(
                node.Id, node.ParentId, [.. children.Select(c => c.Id)], ply, node.San, node.Uci, after.ToFen(),
                node.CommentBefore, node.CommentAfter, ParseNags(node.Nags)));
            for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
        return ([.. _firstMoves.Select(n => n.Id)], dtos);
    }

    public static IReadOnlyList<int> ParseNags(string nags) =>
        [.. nags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)];

    private static Move ParseStoredMove(string uci) =>
        Move.TryParseUci(uci, out var move) ? move : throw new InvalidDataException($"Stored move '{uci}' is not UCI.");
}
