using System.Text;
using System.Text.RegularExpressions;
using ChessDatabase.Api.Games;

namespace ChessDatabase.Api.Pgn;

/// <summary>Conversions between PGN tag values and the Collection's values. Dates: see <see cref="GameDates"/>.</summary>
public static partial class PgnValues
{
    /// <summary>A tag's text, or null when it is empty or PGN's "unknown" ("?") or "not applicable" ("-").</summary>
    public static string? OptionalTag(string? value)
    {
        var text = GameValues.OptionalText(value);
        return text is "?" or "-" ? null : text;
    }

    /// <summary>A missing, zero or unreadable Elo is unrated (null).</summary>
    public static int? Elo(string? value) =>
        int.TryParse(value, out var elo) && elo is > 0 and <= GameValues.MaxElo ? elo : null;

    /// <summary>Escapes a tag value: backslash and quote as PGN does, and line breaks as backslash-n.</summary>
    public static string EscapeTag(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Replace("\r\n", "\n"))
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Reverses <see cref="EscapeTag"/>. Any other backslash is kept as written.</summary>
    public static string UnescapeTag(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] is '\\' or '"' or 'n')
            {
                sb.Append(value[i + 1] == 'n' ? '\n' : value[i + 1]);
                i++;
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// A comment as PGN writes it. Programs wrap long comments over several lines, so a single line
    /// break is just a space; a blank line is a real line break. Line breaks are written that way too.
    /// </summary>
    public static string CommentToPgn(string text) => text.Replace("\r\n", "\n").Replace("\n", "\n\n");

    /// <summary>Reverses <see cref="CommentToPgn"/>: blank lines become line breaks, other whitespace one space.</summary>
    public static string CommentFromPgn(string text)
    {
        var paragraphs = BlankLine().Split(text.Replace("\r\n", "\n"));
        var lines = paragraphs.Select(p => string.Join(' ', p.Split((char[])[' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries)));
        return string.Join('\n', lines).Trim();
    }

    /// <summary>A blank line: a line break, maybe spaces or tabs, another line break.</summary>
    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex BlankLine();
}
