using ChessDatabase.Api.Analysis;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Engines;
using ChessDatabase.Api.Games;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Statistics;

/// <summary>The Owner's score with one opening (ECO) and colour.</summary>
public sealed record OwnerOpeningScoreDto(string? Eco, string? Name, OwnerColour OwnerColour, ScoreDto Score);

/// <summary>The Owner's Accuracy in one analysed Game.</summary>
public sealed record AccuracyPointDto(string Date, double Accuracy, int GameId);

/// <summary>The Owner's statistics ("My statistics"), over the Games the filters keep.</summary>
/// <param name="Openings">By ECO and the Owner's colour, most played first.</param>
/// <param name="RatingHistory">The Owner's Elo as recorded in each Game (unrated and undated Games left out).</param>
/// <param name="AverageOpponentElo">Over rated Opponents only; null when there are none.</param>
/// <param name="RatedOpponentGames">How many Games the average is over.</param>
/// <param name="Accuracy">The Owner's Accuracy per Game, only Games with an Engine Assessment and a date, oldest first.</param>
public sealed record OwnerStatisticsDto(
    ScoreDto Score, ScoreDto White, ScoreDto Black, IReadOnlyList<OwnerOpeningScoreDto> Openings, IReadOnlyList<RatingPointDto> RatingHistory,
    double? AverageOpponentElo, int RatedOpponentGames, IReadOnlyList<AccuracyPointDto> Accuracy);

public static class OwnerStatisticsEndpoints
{
    /// <summary>
    /// Maps GET /api/statistics/owner?from=&amp;to=. The date range means exactly what it means in the
    /// game list (partial dates overlap).
    /// </summary>
    public static void MapOwnerStatisticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/statistics/owner", async (string? from, string? to, CollectionDbContext db) =>
        {
            var query = new GameListQuery(From: GameValues.OptionalText(from), To: GameValues.OptionalText(to));
            if (query.Validate() is { Count: > 0 } errors) return Results.ValidationProblem(errors);
            var games = (await query.RunAsync(db)).Select(r => r.Game).ToList();

            var openings = Figures.ByOpening(games, g => (g.Eco, g.OwnerColour),
                (key, name, score) => new OwnerOpeningScoreDto(key.Eco, name, key.OwnerColour, score));
            var opponentElos = games.Select(g => g.OpponentElo()).OfType<int>().ToList();

            return Results.Ok(new OwnerStatisticsDto(
                ScoreDto.Of(games),
                ScoreDto.Of(games.Where(g => g.OwnerColour == OwnerColour.White)),
                ScoreDto.Of(games.Where(g => g.OwnerColour == OwnerColour.Black)),
                openings,
                Figures.RatingHistory(games, g => g.OwnerElo()),
                opponentElos.Count > 0 ? Math.Round(opponentElos.Average(), 1) : null,
                opponentElos.Count,
                await AccuracyAsync(db, games)));
        });
    }

    /// <summary>The Owner's Accuracy in each dated, analysed Game, worked out from the stored evaluations.</summary>
    private static async Task<List<AccuracyPointDto>> AccuracyAsync(CollectionDbContext db, List<GameRecord> games)
    {
        var dated = games.Where(g => g.Date is not null).ToDictionary(g => g.Id);
        var ids = dated.Keys.ToList();
        var assessments = await db.EngineAssessments.AsNoTracking().Where(a => ids.Contains(a.GameId)).ToListAsync();
        var points = new List<AccuracyPointDto>();
        foreach (var record in assessments)
        {
            var game = dated[record.GameId];
            var assessment = AssessmentQueries.Assess(record);
            var accuracy = game.OwnerColour == OwnerColour.White ? assessment.WhiteAccuracy : assessment.BlackAccuracy;
            if (accuracy is { } value) points.Add(new AccuracyPointDto(game.Date!, Math.Round(value, 1), game.Id));
        }
        return [.. points.OrderBy(p => p.Date, StringComparer.Ordinal).ThenBy(p => p.GameId)];
    }
}
