namespace ChessDatabase.Api.Games;

/// <summary>How a Game ended. Every Game in the Collection has one; "*" (unfinished) does not exist here.</summary>
public enum GameResult
{
    WhiteWins,
    BlackWins,
    Draw,
}

public static class GameResults
{
    /// <summary>The PGN text of a result: "1-0", "0-1" or "1/2-1/2". This is also how the API writes it.</summary>
    public static string ToPgn(this GameResult result) => result switch
    {
        GameResult.WhiteWins => "1-0",
        GameResult.BlackWins => "0-1",
        _ => "1/2-1/2",
    };

    public static bool TryParsePgn(string? text, out GameResult result)
    {
        switch (text)
        {
            case "1-0": result = GameResult.WhiteWins; return true;
            case "0-1": result = GameResult.BlackWins; return true;
            case "1/2-1/2": result = GameResult.Draw; return true;
            default: result = default; return false;
        }
    }

    public static GameResult ParsePgn(string text) =>
        TryParsePgn(text, out var result) ? result : throw new FormatException($"'{text}' is not a Game result.");
}
