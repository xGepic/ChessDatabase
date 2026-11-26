using System.Text.RegularExpressions;

namespace ChessDatabase.Api.Games;

/// <summary>
/// A Game's date may be partial, as in PGN: "2024", "2024-03" or "2024-03-15". Everything that
/// reads, compares or converts such dates lives here.
/// </summary>
public static partial class GameDates
{
    public const string FormatMessage = "Write the date as YYYY, YYYY-MM or YYYY-MM-DD.";

    public static bool IsValid(string date) => Pattern().IsMatch(date.Trim());

    /// <summary>The first and last day a date can stand for: "2025-03" → 2025-03-01 … 2025-03-31.</summary>
    public static (string Start, string End) Range(string date)
    {
        var parts = date.Split('-');
        return parts.Length switch
        {
            1 => ($"{parts[0]}-01-01", $"{parts[0]}-12-31"),
            2 => ($"{date}-01", $"{date}-31"),
            _ => (date, date),
        };
    }

    /// <summary>"2024.03.15" → "2024-03-15", "2024.03.??" → "2024-03", "????.??.??" or unreadable → null.</summary>
    public static string? FromPgn(string? pgnDate)
    {
        if (string.IsNullOrWhiteSpace(pgnDate)) return null;
        var known = new List<string>();
        foreach (var part in pgnDate.Trim().Split('.'))
        {
            if (part.Length == 0 || part.All(c => c == '?')) break;
            if (!part.All(char.IsAsciiDigit)) return null;
            known.Add(part);
        }
        if (known.Count == 0 || known[0].Length != 4) return null;
        var date = string.Join('-', known.Take(3).Select((p, i) => i == 0 ? p : p.PadLeft(2, '0')));
        return IsValid(date) ? date : null;
    }

    /// <summary>"2024-03" → "2024.03.??", null → "????.??.??".</summary>
    public static string ToPgn(string? date)
    {
        var parts = (date ?? "").Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('.',
            parts.ElementAtOrDefault(0) ?? "????",
            parts.ElementAtOrDefault(1) ?? "??",
            parts.ElementAtOrDefault(2) ?? "??");
    }

    [GeneratedRegex(@"^\d{4}(-(0[1-9]|1[0-2])(-(0[1-9]|[12]\d|3[01]))?)?$")]
    private static partial Regex Pattern();
}
