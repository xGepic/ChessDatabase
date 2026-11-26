using System.Text;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;

namespace ChessDatabase.Api.Pgn;

/// <summary>
/// Writes Games as standard PGN with English piece letters: the Seven Tag Roster, Elo tags for
/// rated sides only, the other details, nested Variations, comments and NAGs ($n). Tags and the
/// Owner's notes travel in the extra tags "Tags" and "Notes", which other tools ignore.
/// Engine Assessments are never written (ADR 0003).
/// </summary>
public static class PgnWriter
{
    private const int LineWidth = 80;

    /// <param name="game">Loaded with Opponent, Event, Tags and MoveNodes.</param>
    public static string Write(GameRecord game, string ownerName)
    {
        var sb = new StringBuilder();
        var opponent = game.Opponent.Name;
        var ownerIsWhite = game.OwnerColour == OwnerColour.White;

        Tag(sb, "Event", game.Event?.Name ?? "?");
        Tag(sb, "Site", game.Site ?? "?");
        Tag(sb, "Date", PgnValues.ToPgnDate(game.Date));
        Tag(sb, "Round", game.Round ?? "?");
        Tag(sb, "White", ownerIsWhite ? ownerName : opponent);
        Tag(sb, "Black", ownerIsWhite ? opponent : ownerName);
        Tag(sb, "Result", game.Result.ToPgn());
        if (game.WhiteElo is { } whiteElo) Tag(sb, "WhiteElo", whiteElo.ToString());
        if (game.BlackElo is { } blackElo) Tag(sb, "BlackElo", blackElo.ToString());
        if (game.TimeControl is { } timeControl) Tag(sb, "TimeControl", timeControl);
        if (game.Eco is { } eco) Tag(sb, "ECO", eco);
        if (game.OpeningName is { } opening) Tag(sb, "Opening", opening);
        if (game.Tags.Count > 0) Tag(sb, "Tags", string.Join("; ", game.Tags.Select(t => t.Name).Order(StringComparer.CurrentCultureIgnoreCase)));
        if (game.Notes is { } notes) Tag(sb, "Notes", notes);
        sb.Append('\n');

        var tokens = MovetextTokens(new MoveTree(game.MoveNodes));
        tokens.Add(game.Result.ToPgn());
        AppendWrapped(sb, tokens);
        return sb.ToString();
    }

    public static string WriteMany(IEnumerable<GameRecord> games, string ownerName) =>
        string.Join("\n", games.Select(g => Write(g, ownerName)));

    private static void Tag(StringBuilder sb, string name, string value)
    {
        sb.Append('[').Append(name).Append(" \"").Append(PgnValues.EscapeTag(value)).Append("\"]\n");
    }

    private static List<string> MovetextTokens(MoveTree tree)
    {
        var tokens = new List<string>();
        var firstMoves = tree.ChildrenOf(null);
        if (firstMoves.Count == 0) return tokens;

        var needNumber = true;
        Emit(firstMoves[0]);
        foreach (var alternative in firstMoves.Skip(1))
        {
            WriteVariation(alternative);
            needNumber = true;
        }
        Continue(firstMoves[0]);
        return tokens;

        // The moves after a node along its line, with each alternative in parentheses.
        void Continue(MoveNodeRecord node)
        {
            for (var children = tree.ChildrenOf(node.Id); children.Count > 0; children = tree.ChildrenOf(children[0].Id))
            {
                Emit(children[0]);
                foreach (var alternative in children.Skip(1))
                {
                    WriteVariation(alternative);
                    needNumber = true;
                }
            }
        }

        void WriteVariation(MoveNodeRecord start)
        {
            tokens.Add("(");
            needNumber = true;
            Emit(start);
            Continue(start);
            tokens.Add(")");
        }

        void Emit(MoveNodeRecord node)
        {
            if (node.CommentBefore is { } before)
            {
                tokens.Add(Comment(before));
                needNumber = true;
            }
            var ply = tree.PlyOf(node.Id);
            var number = (ply + 1) / 2;
            if (ply % 2 == 1) tokens.Add($"{number}.");
            else if (needNumber) tokens.Add($"{number}...");
            tokens.Add(node.San);
            tokens.AddRange(node.Nags.Select(n => $"${n}"));
            needNumber = false;
            if (node.CommentAfter is { } after)
            {
                tokens.Add(Comment(after));
                needNumber = true;
            }
        }
    }

    /// <summary>A brace comment; a "}" inside would end it early, so it becomes ")".</summary>
    private static string Comment(string text) => "{" + text.Replace('}', ')').Replace("\r\n", " ").Replace('\n', ' ') + "}";

    /// <summary>Joins tokens with spaces, breaking lines before 80 characters; "(" and ")" hug their moves.</summary>
    private static void AppendWrapped(StringBuilder sb, List<string> tokens)
    {
        var line = new StringBuilder();
        string? previous = null;
        foreach (var token in tokens)
        {
            var glue = previous is null || previous == "(" || token == ")" ? "" : " ";
            var piece = glue + token;
            if (line.Length > 0 && line.Length + piece.Length > LineWidth && glue.Length > 0)
            {
                sb.Append(line).Append('\n');
                line.Clear();
                piece = token;
            }
            line.Append(piece);
            previous = token;
        }
        if (line.Length > 0) sb.Append(line).Append('\n');
    }
}
