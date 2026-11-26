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
    private readonly Dictionary<int, (Position Position, int Ply)> _positionAfter = [];

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
            _positionAfter[item.Node.Id] = (after, item.Ply);
            foreach (var child in ChildrenOf(item.Node.Id)) queue.Enqueue((child, after, item.Ply + 1));
        }
    }

    public static Position StartPosition => Position.Initial;

    public bool Contains(int nodeId) => _nodes.ContainsKey(nodeId);

    public MoveNodeRecord Node(int nodeId) => _nodes[nodeId];

    /// <summary>The ordered continuations of a node; null means the starting position.</summary>
    public IReadOnlyList<MoveNodeRecord> ChildrenOf(int? nodeId) =>
        nodeId is null ? _firstMoves : _children.TryGetValue(nodeId.Value, out var list) ? list : [];

    /// <summary>The position after a node; null means the starting position.</summary>
    public Position PositionAfter(int? nodeId) => nodeId is null ? StartPosition : _positionAfter[nodeId.Value].Position;

    /// <summary>The moves actually played: first continuations from the start.</summary>
    public IReadOnlyList<MoveNodeRecord> MainLine()
    {
        var line = new List<MoveNodeRecord>();
        for (var next = ChildrenOf(null).FirstOrDefault(); next is not null; next = ChildrenOf(next.Id).FirstOrDefault())
            line.Add(next);
        return line;
    }

    /// <summary>The node and every move after it, in any line.</summary>
    public IReadOnlyList<int> Subtree(int nodeId)
    {
        var ids = new List<int>();
        var stack = new Stack<int>([nodeId]);
        while (stack.TryPop(out var id))
        {
            ids.Add(id);
            foreach (var child in ChildrenOf(id)) stack.Push(child.Id);
        }
        return ids;
    }

    /// <summary>
    /// The first move of the innermost Variation containing the node, or null when the node is on
    /// the main line.
    /// </summary>
    public int? VariationStart(int nodeId)
    {
        for (int? id = nodeId; id is { } current; id = _nodes[current].ParentId)
            if (ChildrenOf(_nodes[current].ParentId)[0].Id != current) return current;
        return null;
    }

    /// <summary>
    /// Makes the node's line the main line: the node and each of its ancestors become the first
    /// continuation among their siblings, the others keep their order.
    /// </summary>
    public void PromoteToMainLine(int nodeId)
    {
        for (int? id = nodeId; id is { } current; id = _nodes[current].ParentId)
        {
            var siblings = ChildrenOf(_nodes[current].ParentId);
            if (siblings[0].Id == current) continue;
            var reordered = siblings.OrderBy(n => n.Id == current ? 0 : 1).ThenBy(n => n.Order).ToList();
            for (var i = 0; i < reordered.Count; i++) reordered[i].Order = i;
        }
    }

    public (IReadOnlyList<int> FirstMoves, IReadOnlyList<MoveNodeDto> Nodes) ToDtos()
    {
        var dtos = new List<MoveNodeDto>(_nodes.Count);
        var stack = new Stack<MoveNodeRecord>(_firstMoves.AsEnumerable().Reverse());
        while (stack.TryPop(out var node))
        {
            var children = ChildrenOf(node.Id);
            var (after, ply) = _positionAfter[node.Id];
            dtos.Add(new MoveNodeDto(
                node.Id, node.ParentId, [.. children.Select(c => c.Id)], ply, node.San, node.Uci, after.ToFen(),
                node.CommentBefore, node.CommentAfter, node.Nags));
            for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
        return ([.. _firstMoves.Select(n => n.Id)], dtos);
    }

    private static Move ParseStoredMove(string uci) =>
        Move.TryParseUci(uci, out var move) ? move : throw new InvalidDataException($"Stored move '{uci}' is not UCI.");
}
