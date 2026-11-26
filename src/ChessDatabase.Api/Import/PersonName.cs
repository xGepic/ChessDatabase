using System.Globalization;
using System.Text;

namespace ChessDatabase.Api.Import;

/// <summary>
/// A person's name split into surname and given names, so that "Mustermann, Max",
/// "Mustermann M." and "Max Mustermann" can be recognised as the same person.
/// </summary>
public sealed record PersonName(string Surname, IReadOnlyList<string> Given)
{
    public static PersonName Parse(string name)
    {
        var text = Normalise(name);
        var comma = text.IndexOf(',');
        if (comma >= 0)
            return new PersonName(text[..comma].Trim(), Words(text[(comma + 1)..]));

        var words = Words(text);
        if (words.Count == 0) return new PersonName("", []);
        if (words.Count == 1) return new PersonName(words[0], []);

        // "Mustermann M." puts the initials last; "Max Mustermann" puts the surname last.
        if (words.Skip(1).All(IsInitial)) return new PersonName(words[0], words.Skip(1).ToList());
        return new PersonName(words[^1], words.Take(words.Count - 1).ToList());
    }

    /// <summary>
    /// Same surname, and given names that agree: the first initials match, and if both first names
    /// are written out in full, they are the same. A missing given name never contradicts.
    /// </summary>
    public bool Matches(PersonName other)
    {
        if (Surname.Length == 0 || Surname != other.Surname) return false;
        if (Given.Count == 0 || other.Given.Count == 0) return true;
        string mine = Given[0], theirs = other.Given[0];
        if (mine[0] != theirs[0]) return false;
        return IsInitial(mine) || IsInitial(theirs) || mine == theirs;
    }

    public static bool SamePerson(string a, string b) => Parse(a).Matches(Parse(b));

    private static bool IsInitial(string word) => word.Length == 1;

    private static List<string> Words(string text) =>
        [.. text.Split([' ', '.'], StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>Lower case, accents removed ("Jörg" → "jorg"), dots treated as word breaks.</summary>
    private static string Normalise(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).Replace('.', ' ');
    }
}
