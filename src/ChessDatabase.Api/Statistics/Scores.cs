using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;

namespace ChessDatabase.Api.Statistics;

/// <summary>The Owner's score over some Games: wins, draws and losses from the Owner's side.</summary>
/// <param name="Percent">Points (a draw counts half) per Game, in percent; 0 when there are no Games.</param>
public sealed record ScoreDto(int Games, int Wins, int Draws, int Losses, double Percent)
{
    public static ScoreDto Of(IEnumerable<GameRecord> games)
    {
        int wins = 0, draws = 0, losses = 0;
        foreach (var game in games)
        {
            switch (game.ResultForOwner())
            {
                case OwnerResult.Win: wins++; break;
                case OwnerResult.Draw: draws++; break;
                default: losses++; break;
            }
        }
        var count = wins + draws + losses;
        return new ScoreDto(count, wins, draws, losses, count == 0 ? 0 : Math.Round((wins + draws / 2.0) / count * 100, 1));
    }
}

/// <summary>A rating at a point in time, from the Elo recorded in one Game.</summary>
/// <param name="Date">The Game's date as recorded ("2024-03-05", or partial).</param>
public sealed record RatingPointDto(string Date, int Elo, int GameId);

/// <summary>The Owner's score with one opening (by ECO); Name is the opening's name from the Owner's Games.</summary>
public sealed record OpeningScoreDto(string? Eco, string? Name, ScoreDto Score);

/// <summary>Figures shared by the Dossier and the Owner's statistics.</summary>
public static class Figures
{
    /// <summary>
    /// A rating over time from the Elo one side had in each Game. Unrated Games are left out, and
    /// so are Games without a date: they can't be placed on the time line.
    /// </summary>
    public static List<RatingPointDto> RatingHistory(IEnumerable<GameRecord> games, Func<GameRecord, int?> elo) =>
        [.. games
            .Where(g => g.Date is not null && elo(g) is not null)
            .OrderBy(g => g.Date, StringComparer.Ordinal).ThenBy(g => g.CreatedAt)
            .Select(g => new RatingPointDto(g.Date!, elo(g)!.Value, g.Id))];

    /// <summary>
    /// The Owner's score per opening, grouped by <paramref name="key"/> (the ECO, perhaps with
    /// more), most played first, then by ECO; unclassified Games together under no ECO.
    /// <paramref name="row"/> makes a row from the key, the opening's name in those Games and the score.
    /// </summary>
    public static List<T> ByOpening<TKey, T>(IEnumerable<GameRecord> games, Func<GameRecord, TKey> key, Func<TKey, string?, ScoreDto, T> row) =>
        [.. games
            .GroupBy(key)
            .Select(g => (g.Key, g.First().Eco, Name: g.Select(x => x.OpeningName).FirstOrDefault(n => n is not null), Score: ScoreDto.Of(g)))
            .OrderByDescending(o => o.Score.Games).ThenBy(o => o.Eco ?? "~", StringComparer.Ordinal).ThenBy(o => o.Key)
            .Select(o => row(o.Key, o.Name, o.Score))];

    /// <summary>Newest first; Games without a date last.</summary>
    public static IOrderedEnumerable<GameRecord> NewestFirst(IEnumerable<GameRecord> games) =>
        games.OrderByDescending(g => g.Date ?? "", StringComparer.Ordinal).ThenByDescending(g => g.CreatedAt);
}
