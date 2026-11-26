using System.Text;
using ChessDatabase.Api.Games;

namespace ChessDatabase.Api.Pgn;

/// <summary>Conversions between PGN tag values and the Collection's values.</summary>
public static class PgnValues
{
    /// <summary>"2024.03.15" → "2024-03-15", "2024.03.??" → "2024-03", "????.??.??" → null.</summary>
    public static string? ToGameDate(string? pgnDate)
    {
        if (string.IsNullOrWhiteSpace(pgnDate)) return null;
        var parts = pgnDate.Trim().Split('.');
        var known = new List<string>();
        foreach (var part in parts)
        {
            if (part.Length == 0 || part.All(c => c == '?')) break;
            if (!part.All(char.IsAsciiDigit)) return null;
            known.Add(part);
        }
        if (known.Count == 0 || known[0].Length != 4) return null;
        var date = string.Join('-', known.Take(3).Select((p, i) => i == 0 ? p : p.PadLeft(2, '0')));
        return GameEndpoints.IsValidDate(date) ? date : null;
    }

    /// <summary>"2024-03" → "2024.03.??", null → "????.??.??".</summary>
    public static string ToPgnDate(string? gameDate)
    {
        var parts = (gameDate ?? "").Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('.',
            parts.ElementAtOrDefault(0) ?? "????",
            parts.ElementAtOrDefault(1) ?? "??",
            parts.ElementAtOrDefault(2) ?? "??");
    }

    /// <summary>PGN uses "?" for unknown and "-" for not applicable; both mean "not set" here.</summary>
    public static string? Text(string? value)
    {
        var text = GameEndpoints.Text(value);
        return text is "?" or "-" ? null : text;
    }

    /// <summary>A missing, zero or unreadable Elo is unrated (null).</summary>
    public static int? Elo(string? value) =>
        int.TryParse(value, out var elo) && elo is > 0 and <= GameEndpoints.MaxElo ? elo : null;

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
}
