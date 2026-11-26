using System.Text;
using System.Text.RegularExpressions;

namespace ChessDatabase.Api.Pgn;

/// <summary>A move as written in PGN, with its Annotations and the Variations that replace it.</summary>
public sealed class PgnMove(string san)
{
    public string San { get; } = san;
    public List<int> Nags { get; } = [];
    public string? CommentBefore { get; set; }
    public string? CommentAfter { get; set; }

    /// <summary>Alternative lines starting in place of this move.</summary>
    public List<List<PgnMove>> Variations { get; } = [];
}

/// <summary>
/// One entry of a PGN file: its tags and its moves, not yet checked against Chess Rules. It becomes
/// a Game only once it is complete (both names, a result) and assigned to the Owner.
/// </summary>
public sealed class PgnEntry
{
    public Dictionary<string, string> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PgnMove> Moves { get; } = [];

    /// <summary>The result written after the moves ("1-0", "0-1", "1/2-1/2", "*"), if any.</summary>
    public string? Termination { get; set; }

    /// <summary>The Game's own PGN text, as read.</summary>
    public string Source { get; set; } = "";

    public string? Tag(string name) => Tags.TryGetValue(name, out var value) ? value : null;
}

/// <summary>An entry that could be read, or the reason it could not.</summary>
public sealed record PgnReadResult(int Index, PgnEntry? Entry, string? Error, string Source);

/// <summary>
/// Reads PGN text with any number of Games. Each Game is read on its own, so one malformed Game
/// never stops the others: it comes back as a result with an error.
/// </summary>
public static partial class PgnReader
{
    private static readonly Dictionary<string, int> SuffixNags = new()
    {
        ["!"] = 1, ["?"] = 2, ["!!"] = 3, ["??"] = 4, ["!?"] = 5, ["?!"] = 6,
    };

    private static readonly string[] Results = ["1-0", "0-1", "1/2-1/2", "*"];

    public static IReadOnlyList<PgnReadResult> Read(string text)
    {
        var results = new List<PgnReadResult>();
        var index = 0;
        foreach (var chunk in SplitGames(text))
        {
            index++;
            try
            {
                results.Add(new PgnReadResult(index, ParseGame(chunk), null, chunk));
            }
            catch (FormatException e)
            {
                results.Add(new PgnReadResult(index, null, e.Message, chunk));
            }
        }
        return results;
    }

    /// <summary>
    /// Splits the text into Games: a Game starts at a tag line ("[Name ...]") that follows move text.
    /// Lines starting with "%" are PGN escape lines and are ignored.
    /// </summary>
    private static IEnumerable<string> SplitGames(string text)
    {
        var current = new StringBuilder();
        var hasMoves = false;
        var inComment = false;
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimStart('﻿');
            if (!inComment && line.StartsWith('%')) continue;
            var trimmed = line.Trim();
            if (!inComment && trimmed.StartsWith('[') && hasMoves)
            {
                yield return current.ToString();
                current.Clear();
                hasMoves = false;
            }
            current.Append(line).Append('\n');
            if (!inComment && !trimmed.StartsWith('[') && trimmed.Length > 0) hasMoves = true;
            inComment = UpdateCommentState(line, inComment);
        }
        if (current.ToString().Trim().Length > 0) yield return current.ToString();
    }

    /// <summary>Whether a brace comment is still open at the end of the line.</summary>
    private static bool UpdateCommentState(string line, bool inComment)
    {
        foreach (var c in line)
        {
            if (inComment) { if (c == '}') inComment = false; }
            else if (c == '{') inComment = true;
            else if (c == ';') break; // the rest of the line is a comment
        }
        return inComment;
    }

    private static PgnEntry ParseGame(string chunk)
    {
        var game = new PgnEntry { Source = chunk.Trim() };
        var movetext = new StringBuilder();
        var inTags = true;
        foreach (var line in chunk.Split('\n'))
        {
            var trimmed = line.Trim();
            if (inTags && trimmed.StartsWith('['))
            {
                var match = TagPattern().Match(trimmed);
                if (!match.Success) throw new FormatException($"Unreadable tag line: {trimmed}");
                game.Tags[match.Groups[1].Value] = PgnValues.UnescapeTag(match.Groups[2].Value);
                continue;
            }
            if (trimmed.Length > 0) inTags = false;
            movetext.Append(line).Append('\n');
        }

        new MovetextParser(movetext.ToString(), game).Parse();
        if (game.Moves.Count == 0 && game.Termination is null && game.Tags.Count == 0)
            throw new FormatException("This is not a PGN Game.");
        return game;
    }

    [GeneratedRegex("""^\[\s*([A-Za-z0-9_]+)\s+"((?:[^"\\]|\\.)*)"\s*\]$""")]
    private static partial Regex TagPattern();

    /// <summary>Reads move text: moves, numbers, comments, NAGs, Variations and the result.</summary>
    private sealed class MovetextParser(string text, PgnEntry game)
    {
        private int _pos;

        public void Parse()
        {
            ParseLine(game.Moves, topLevel: true);
        }

        private void ParseLine(List<PgnMove> line, bool topLevel)
        {
            string? pendingComment = null;
            while (true)
            {
                SkipWhitespace();
                if (_pos >= text.Length)
                {
                    if (!topLevel) throw new FormatException("A Variation is missing its closing ')'.");
                    AttachTrailingComment(line, pendingComment);
                    return;
                }

                var c = text[_pos];
                if (c == '{')
                {
                    var comment = ReadBraceComment();
                    if (line.Count == 0) pendingComment = Join(pendingComment, comment);
                    else line[^1].CommentAfter = Join(line[^1].CommentAfter, comment);
                }
                else if (c == ';')
                {
                    var end = text.IndexOf('\n', _pos);
                    var comment = text[(_pos + 1)..(end < 0 ? text.Length : end)].Trim();
                    _pos = end < 0 ? text.Length : end;
                    if (comment.Length == 0) continue;
                    if (line.Count == 0) pendingComment = Join(pendingComment, comment);
                    else line[^1].CommentAfter = Join(line[^1].CommentAfter, comment);
                }
                else if (c == '(')
                {
                    if (line.Count == 0) throw new FormatException("A Variation must follow a move.");
                    _pos++;
                    var variation = new List<PgnMove>();
                    ParseLine(variation, topLevel: false);
                    if (variation.Count > 0) line[^1].Variations.Add(variation);
                }
                else if (c == ')')
                {
                    if (topLevel) throw new FormatException("A ')' closes a Variation that was never opened.");
                    _pos++;
                    AttachTrailingComment(line, pendingComment);
                    return;
                }
                else if (c == '$')
                {
                    _pos++;
                    var number = ReadWhile(char.IsDigit);
                    if (line.Count == 0 || !int.TryParse(number, out var nag)) throw new FormatException($"Unreadable symbol ${number}.");
                    line[^1].Nags.Add(nag);
                }
                else
                {
                    var token = ReadWhile(ch => !char.IsWhiteSpace(ch) && ch is not ('{' or '(' or ')' or ';' or '$'));
                    if (token.Length == 0) throw new FormatException($"Unexpected '{c}' in the moves.");
                    if (Results.Contains(token))
                    {
                        if (!topLevel) throw new FormatException("A result inside a Variation.");
                        // Anything after the result belongs to no move and is ignored.
                        game.Termination = token;
                        AttachTrailingComment(line, pendingComment);
                        return;
                    }
                    if (MoveNumberPattern().IsMatch(token)) continue;
                    AddMove(line, token, ref pendingComment);
                }
            }
        }

        private static void AddMove(List<PgnMove> line, string token, ref string? pendingComment)
        {
            // "12.e4" (number glued to the move) and "e4!?" (suffix symbols) are both common.
            var numbered = GluedNumberPattern().Match(token);
            if (numbered.Success) token = token[numbered.Length..];
            var suffix = SuffixPattern().Match(token);
            var san = suffix.Success ? token[..suffix.Index] : token;
            if (san.Length == 0) throw new FormatException($"'{token}' is not a move.");

            var move = new PgnMove(san) { CommentBefore = pendingComment };
            pendingComment = null;
            if (suffix.Success)
            {
                if (!SuffixNags.TryGetValue(suffix.Value, out var nag)) throw new FormatException($"'{token}' is not a move.");
                move.Nags.Add(nag);
            }
            line.Add(move);
        }

        private static void AttachTrailingComment(List<PgnMove> line, string? comment)
        {
            if (comment is null) return;
            if (line.Count == 0) return;
            line[^1].CommentAfter = Join(line[^1].CommentAfter, comment);
        }

        private string ReadBraceComment()
        {
            var end = text.IndexOf('}', _pos + 1);
            if (end < 0) throw new FormatException("A comment is missing its closing '}'.");
            var comment = text[(_pos + 1)..end];
            _pos = end + 1;
            return PgnValues.CommentFromPgn(comment);
        }

        private string ReadWhile(Func<char, bool> predicate)
        {
            var start = _pos;
            while (_pos < text.Length && predicate(text[_pos])) _pos++;
            return text[start.._pos];
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }

        private static string? Join(string? first, string second) =>
            string.IsNullOrEmpty(second) ? first : string.IsNullOrEmpty(first) ? second : $"{first} {second}";
    }

    [GeneratedRegex(@"^\d+\.+$")]
    private static partial Regex MoveNumberPattern();

    [GeneratedRegex(@"^\d+\.+")]
    private static partial Regex GluedNumberPattern();

    [GeneratedRegex(@"[!?]+$")]
    private static partial Regex SuffixPattern();
}
