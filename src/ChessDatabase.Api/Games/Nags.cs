namespace ChessDatabase.Api.Games;

/// <summary>
/// Numeric Annotation Glyphs. A move carries at most one move symbol (NAG 1–6: ! ? !! ?? !? ?!)
/// and at most one position symbol (NAG 10–19: = ∞ ⩲ ⩱ ± ∓ +- -+ and their relatives).
/// </summary>
public static class Nags
{
    public const int Good = 1, Mistake = 2, Brilliant = 3, Blunder = 4, Interesting = 5, Dubious = 6;

    public static bool IsMoveSymbol(int nag) => nag is >= 1 and <= 6;

    public static bool IsPositionSymbol(int nag) => nag is >= 10 and <= 19;

    /// <summary>Null when the symbols can go on one move, otherwise why not.</summary>
    public static string? Validate(IReadOnlyCollection<int> nags)
    {
        if (nags.Any(n => n is < 1 or > 255)) return "Symbols are NAG numbers from 1 to 255.";
        var distinct = nags.Distinct().ToList();
        if (distinct.Count(IsMoveSymbol) > 1) return "A move takes at most one move symbol.";
        if (distinct.Count(IsPositionSymbol) > 1) return "A move takes at most one position symbol.";
        return null;
    }
}
