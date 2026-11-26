using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Statistics;

/// <summary>A Player with the details the Owner recorded.</summary>
public sealed record PlayerDetailsDto(int Id, string Name, string? FideId, string? Club)
{
    public static PlayerDetailsDto Of(PlayerRecord p) => new(p.Id, p.Name, p.FideId, p.Club);
}

/// <summary>A Player's Dossier: everything about the Owner's Games against them.</summary>
/// <param name="Score">The Owner's head-to-head score against the Player.</param>
/// <param name="RatingHistory">The Player's Elo as recorded in those Games (unrated and undated Games left out).</param>
/// <param name="Openings">The openings of those Games (by ECO) with the Owner's score, most played first.</param>
/// <param name="Games">Newest first.</param>
public sealed record DossierDto(
    PlayerDetailsDto Player, ScoreDto Score, IReadOnlyList<RatingPointDto> RatingHistory, IReadOnlyList<OpeningScoreDto> Openings,
    IReadOnlyList<GameSummaryDto> Games);

/// <summary>A row of the Players list.</summary>
/// <param name="LastDate">The date of the newest dated Game against them.</param>
public sealed record PlayerRowDto(int Id, string Name, string? FideId, string? Club, ScoreDto Score, string? LastDate);

public static class DossierEndpoints
{
    /// <summary>Maps GET /api/players/overview?search= and GET /api/players/{id}/dossier.</summary>
    public static void MapDossierEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/players/overview", async (string? search, CollectionDbContext db) =>
        {
            var term = search?.Trim() ?? "";
            var players = (await db.Players.AsNoTracking().ToListAsync())
                .Where(p => term.Length == 0 || p.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
            var ids = players.Select(p => p.Id).ToList();
            var games = (await db.Games.AsNoTracking().Where(g => ids.Contains(g.OpponentId)).ToListAsync()).ToLookup(g => g.OpponentId);
            return players
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new PlayerRowDto(p.Id, p.Name, p.FideId, p.Club, ScoreDto.Of(games[p.Id]),
                    games[p.Id].Select(g => g.Date).Where(d => d is not null).Max(StringComparer.Ordinal)))
                .ToList();
        });

        app.MapGet("/api/players/{id:int}/dossier", async (int id, CollectionDbContext db) =>
        {
            var player = await db.Players.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
            if (player is null) return Results.NotFound();
            var games = await db.Games.WithDetails().Where(g => g.OpponentId == id).ToListAsync();
            var ownerName = await GameQueries.OwnerNameAsync(db);
            var ids = games.Select(g => g.Id).ToList();
            var analysed = (await db.EngineAssessments.Where(a => ids.Contains(a.GameId)).Select(a => a.GameId).ToListAsync()).ToHashSet();
            return Results.Ok(new DossierDto(
                PlayerDetailsDto.Of(player),
                ScoreDto.Of(games),
                Figures.RatingHistory(games, g => g.OpponentElo()),
                Figures.ByOpening(games, g => g.Eco, (eco, name, score) => new OpeningScoreDto(eco, name, score)),
                [.. Figures.NewestFirst(games).Select(g => GameQueries.ToSummary(g, ownerName, analysed.Contains(g.Id)))]));
        });
    }
}
