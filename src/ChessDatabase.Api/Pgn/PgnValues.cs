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
        return Games.GameEndpoints.IsValidDate(date) ? date : null;
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
        var text = Games.GameEndpoints.Text(value);
        return text is "?" or "-" ? null : text;
    }

    /// <summary>A missing, zero or unreadable Elo is unrated (null).</summary>
    public static int? Elo(string? value) =>
        int.TryParse(value, out var elo) && elo is > 0 and <= Games.GameEndpoints.MaxElo ? elo : null;
}
