namespace ChessDatabase.Api.Games;

/// <summary>Rules for the values of a Game's details, shared by entry, import and the game list.</summary>
public static class GameValues
{
    public const int MaxElo = 3500;

    public static readonly string EloOutOfRange = $"An Elo is between 1 and {MaxElo}, or empty/0 for unrated.";

    /// <summary>Trimmed text, or null when nothing is left: optional details are either set or absent.</summary>
    public static string? OptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>An Elo left empty or entered as 0 means unrated (null).</summary>
    public static int? Unrated(int? elo) => elo is null or 0 ? null : elo;
}

/// <summary>Names of Players, Events and Tags are the same when they differ only in letter case.</summary>
public static class Names
{
    public static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>The Owner is always one side of a Game; these turn "Owner/Opponent" into "White/Black" and back.</summary>
public static class PlayerSides
{
    public static (T White, T Black) ToWhiteAndBlack<T>(Collection.OwnerColour ownerColour, T owner, T opponent) =>
        ownerColour == Collection.OwnerColour.White ? (owner, opponent) : (opponent, owner);

    public static (T Owner, T Opponent) ToOwnerAndOpponent<T>(Collection.OwnerColour ownerColour, T white, T black) =>
        ownerColour == Collection.OwnerColour.White ? (white, black) : (black, white);
}
