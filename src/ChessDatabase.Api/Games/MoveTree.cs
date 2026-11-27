using ChessDatabase.Api.Collection;
using ChessDatabase.Rules;

namespace ChessDatabase.Api.Games;

/// <summary>A stored move in a move tree: a Game's or a Repertoire's.</summary>
public interface ITreeNode
{
    int Id { get; }

    /// <summary>Null for a move from the starting position.</summary>
    int? ParentId { get; }

    /// <summary>Among siblings, 0 is the main continuation; later ones are Variations.</summary>
    int Order { get; set; }

    string Uci { get; }

    string San { get; }
}

/// <summary>
/// A move tree loaded from its stored nodes (a Game's, a Repertoire's). Positions are replayed
/// through Chess Rules, so every node's position comes from the rules, never from stored text.
/// Variations behave the same in every tree: the first child is the main continuation.
/// </summary>
public class MoveTree<TNode> where TNode : class, ITreeNode
{
    private readonly Dictionary<int, TNode> _nodes;
    private readonly Dictionary<int, List<TNode>> _children = [];
    private readonly List<TNode> _firstMoves;
    private readonly Dictionary<int, (Position Position, int Ply)> _positionAfter = [];

    public MoveTree(IEnumerable<TNode> nodes)
    {
        _nodes = nodes.ToDictionary(n => n.Id);
        foreach (var group in _nodes.Values.Where(n => n.ParentId is not null).GroupBy(n => n.ParentId!.Value))
            _children[group.Key] = [.. group.OrderBy(n => n.Order)];
        _firstMoves = [.. _nodes.Values.Where(n => n.ParentId is null).OrderBy(n => n.Order)];

        var queue = new Queue<(TNode Node, Position Before, int Ply)>(_firstMoves.Select(n => (n, StartPosition, 1)));
        while (queue.TryDequeue(out var item))
        {
            var after = item.Before.Play(Move.ParseUci(item.Node.Uci));
            _positionAfter[item.Node.Id] = (after, item.Ply);
            foreach (var child in ChildrenOf(item.Node.Id)) queue.Enqueue((child, after, item.Ply + 1));
        }
    }

    public static Position StartPosition => Position.Initial;

    public bool Contains(int nodeId) => _nodes.ContainsKey(nodeId);

    public TNode Node(int nodeId) => _nodes[nodeId];

    /// <summary>The ordered continuations of a node; null means the starting position.</summary>
    public IReadOnlyList<TNode> ChildrenOf(int? nodeId) =>
        nodeId is null ? _firstMoves : _children.TryGetValue(nodeId.Value, out var list) ? list : [];

    /// <summary>The position after a node; null means the starting position.</summary>
    public Position PositionAfter(int? nodeId) => nodeId is null ? StartPosition : _positionAfter[nodeId.Value].Position;

    /// <summary>The half-move number of a node: 1 for White's first move.</summary>
    public int PlyOf(int nodeId) => _positionAfter[nodeId].Ply;

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

    /// <summary>
    /// Plays a checked line (read from the position after the parent) move by move. A move already in
    /// the tree there is followed; the first new one starts a new line after the existing continuations.
    /// <paramref name="create"/> makes each new node and adds it wherever the caller keeps nodes;
    /// nothing is saved here. Returns the line's last node and whether anything is new.
    /// </summary>
    public (TNode Last, bool Added) AddLine(int? parentId, Line line, Func<NewMove<TNode>, TNode> create)
    {
        if (line.Count == 0) throw new ArgumentException("A line has at least one move.", nameof(line));
        int? at = parentId;
        TNode? last = null, firstNew = null;
        foreach (var (uci, san) in line.Uci.Zip(line.San))
        {
            if (firstNew is null && ChildrenOf(at).FirstOrDefault(n => n.Uci == uci) is { } existing)
            {
                last = existing;
                at = existing.Id;
                continue;
            }
            if (firstNew is null)
            {
                var siblings = ChildrenOf(at);
                last = firstNew = create(new NewMove<TNode>(at, null, siblings.Count == 0 ? 0 : siblings.Max(n => n.Order) + 1, uci, san));
            }
            else last = create(new NewMove<TNode>(null, last, 0, uci, san));
        }
        return (last!, firstNew is not null);
    }

    /// <summary>The tree as the API shows it: every node in reading order, with its position and annotations.</summary>
    protected (IReadOnlyList<int> FirstMoves, IReadOnlyList<MoveNodeDto> Nodes) ToDtos(
        Func<TNode, (string? Before, string? After, IReadOnlyList<int> Nags)> annotations)
    {
        var dtos = new List<MoveNodeDto>(_nodes.Count);
        var stack = new Stack<TNode>(_firstMoves.AsEnumerable().Reverse());
        while (stack.TryPop(out var node))
        {
            var children = ChildrenOf(node.Id);
            var (after, ply) = _positionAfter[node.Id];
            var (before, afterComment, nags) = annotations(node);
            dtos.Add(new MoveNodeDto(
                node.Id, node.ParentId, [.. children.Select(c => c.Id)], ply, node.San, node.Uci, after.ToFen(), before, afterComment, [.. nags]));
            for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
        return ([.. _firstMoves.Select(n => n.Id)], dtos);
    }
}

/// <summary>
/// A node <see cref="MoveTree{TNode}.AddLine"/> asks the caller to create: under a stored parent
/// (<paramref name="ParentId"/>, null for the start) or, further along a new line, under a node
/// created just before (<paramref name="Parent"/>).
/// </summary>
public sealed record NewMove<TNode>(int? ParentId, TNode? Parent, int Order, string Uci, string San) where TNode : class;

/// <summary>A Game's move tree.</summary>
public sealed class MoveTree(IEnumerable<MoveNodeRecord> nodes) : MoveTree<MoveNodeRecord>(nodes)
{
    /// <summary>The first continuations from the start: the moves actually played.</summary>
    public IReadOnlyList<MoveNodeRecord> MainLine()
    {
        var line = new List<MoveNodeRecord>();
        for (var children = ChildrenOf(null); children is [var next, ..]; children = ChildrenOf(next.Id))
            line.Add(next);
        return line;
    }

    public (IReadOnlyList<int> FirstMoves, IReadOnlyList<MoveNodeDto> Nodes) ToDtos() =>
        ToDtos(n => (n.CommentBefore, n.CommentAfter, n.Nags));
}
