using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;

namespace ChessDatabase.Api.Export;

public enum NotationStyle
{
    /// <summary>A main-line move: printed bold.</summary>
    MainMove,
    VariationMove,
    Comment,
}

/// <summary>A word or move of printed notation. "(" and ")" hug their neighbours.</summary>
public sealed record NotationToken(string Text, NotationStyle Style);

public abstract record NotationBlock;

/// <summary>A paragraph of notation; Variations of the main line get their own, indented by one step.</summary>
public sealed record NotationParagraph(int Indent, IReadOnlyList<NotationToken> Tokens) : NotationBlock;

/// <summary>A board diagram of the position after a move.</summary>
public sealed record NotationDiagram(Position Position, Move? LastMove, string Caption) : NotationBlock;

/// <summary>
/// Lays out a Game's notation for print, book style: the main line in bold, each Variation of
/// it in its own indented paragraph, deeper Variations inline in parentheses. Comments and
/// symbols sit with their moves, and a diagram follows every move marked for one (inside
/// parentheses, it waits until the paragraph ends so the brackets stay together).
/// </summary>
public static class NotationLayout
{
    public static IReadOnlyList<NotationBlock> Build(MoveTree tree, string result, bool finalDiagram)
    {
        var blocks = new List<NotationBlock>();
        var tokens = new List<NotationToken>();
        var heldDiagrams = new List<NotationDiagram>();
        var indent = 0;
        var needNumber = true;

        var firstMoves = tree.ChildrenOf(null);
        if (firstMoves.Count > 0) Line(firstMoves, 0);
        tokens.Add(new NotationToken(result, NotationStyle.MainMove));
        Break();

        var mainLine = tree.MainLine();
        if (finalDiagram && mainLine.Count > 0 && !mainLine[^1].Nags.Contains(Nags.Diagram))
            blocks.Add(new NotationDiagram(tree.PositionAfter(mainLine[^1].Id), LastMove(mainLine[^1]), "Final position"));
        return blocks;

        // The first of the siblings, its alternatives, then the line on along first continuations.
        void Line(IReadOnlyList<MoveNodeRecord> siblings, int depth)
        {
            Emit(siblings[0], depth);
            Alternatives(siblings, depth);
            for (var children = tree.ChildrenOf(siblings[0].Id); children.Count > 0; children = tree.ChildrenOf(children[0].Id))
            {
                Emit(children[0], depth);
                Alternatives(children, depth);
            }
        }

        void Alternatives(IReadOnlyList<MoveNodeRecord> siblings, int depth)
        {
            foreach (var alternative in siblings.Skip(1))
            {
                if (depth == 0)
                {
                    Break();
                    indent = 1;
                    needNumber = true;
                    Line([alternative], 1);
                    Break();
                    indent = 0;
                }
                else
                {
                    tokens.Add(new NotationToken("(", NotationStyle.VariationMove));
                    needNumber = true;
                    Line([alternative], depth + 1);
                    tokens.Add(new NotationToken(")", NotationStyle.VariationMove));
                }
                needNumber = true;
            }
        }

        void Emit(MoveNodeRecord node, int depth)
        {
            if (node.CommentBefore is { } before)
            {
                tokens.Add(new NotationToken(before, NotationStyle.Comment));
                needNumber = true;
            }
            var style = depth == 0 ? NotationStyle.MainMove : NotationStyle.VariationMove;
            var label = MoveLabel(tree, node, needNumber);
            var moveSymbol = string.Concat(node.Nags.Where(Nags.IsMoveSymbol).Select(Nags.Glyph));
            tokens.Add(new NotationToken(label + moveSymbol, style));
            foreach (var nag in node.Nags.Where(n => !Nags.IsMoveSymbol(n)))
                if (Nags.Glyph(nag) is { } glyph) tokens.Add(new NotationToken(glyph, style));
            needNumber = false;
            if (node.CommentAfter is { } after)
            {
                tokens.Add(new NotationToken(after, NotationStyle.Comment));
                needNumber = true;
            }
            if (node.Nags.Contains(Nags.Diagram))
            {
                var diagram = new NotationDiagram(tree.PositionAfter(node.Id), LastMove(node), $"After {MoveLabel(tree, node, true)}");
                if (depth >= 2)
                {
                    heldDiagrams.Add(diagram);
                    return;
                }
                Break();
                blocks.Add(diagram);
                needNumber = true;
            }
        }

        void Break()
        {
            if (tokens.Count > 0) blocks.Add(new NotationParagraph(indent, [.. tokens]));
            tokens.Clear();
            blocks.AddRange(heldDiagrams);
            heldDiagrams.Clear();
        }
    }

    /// <summary>"2.Nf3", "2...d6", or just "d6" when Black's move needs no number.</summary>
    private static string MoveLabel(MoveTree tree, MoveNodeRecord node, bool withNumber)
    {
        var ply = tree.PlyOf(node.Id);
        var number = (ply + 1) / 2;
        if (ply % 2 == 1) return $"{number}.{node.San}";
        return withNumber ? $"{number}...{node.San}" : node.San;
    }

    private static Move? LastMove(MoveNodeRecord node) => Move.TryParseUci(node.Uci, out var move) ? move : null;
}
