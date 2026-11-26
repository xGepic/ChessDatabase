using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Research;

/// <summary>A move played from the position in the Owner's Games.</summary>
/// <param name="Fen">The position after the move, so the UI can show it without saving anything.</param>
/// <param name="GameCount">Games in which it was played from this position.</param>
/// <param name="Score">The Owner's score in those Games, in percent (a draw counts half).</param>
public sealed record TreeMoveDto(string Uci, string San, string Fen, int GameCount, int Wins, int Draws, int Losses, double Score);

/// <summary>A Game that reached the position in its main line.</summary>
/// <param name="Ply">Half-moves played when the position first occurred (0: the starting position).</param>
/// <param name="MoveNumber">The number of the move played next from the position.</param>
public sealed record PositionGameDto(GameSummaryDto Game, int Ply, int MoveNumber);

/// <summary>The Opening Tree for one position, from the Owner's Games.</summary>
/// <param name="GameCount">Games that reached the position.</param>
/// <param name="Moves">Most played first.</param>
/// <param name="Games">The newest of those Games, at most <see cref="ResearchEndpoints.TreeGameLimit"/>.</param>
public sealed record OpeningTreeDto(int GameCount, IReadOnlyList<TreeMoveDto> Moves, IReadOnlyList<PositionGameDto> Games);

/// <summary>Position Search results.</summary>
/// <param name="GameCount">Games that reached the position.</param>
/// <param name="Games">The newest of them, at most <see cref="ResearchEndpoints.SearchGameLimit"/>.</param>
public sealed record PositionSearchDto(int GameCount, IReadOnlyList<PositionGameDto> Games);

/// <summary>
/// Research over the position index: the Opening Tree (moves played from a position in the
/// Owner's Games) and Position Search (Games whose main line reached a position). Only main-line
/// positions are indexed, so Variations never count. The position is given as a FEN; positions
/// reached by different move orders are the same position.
/// </summary>
public static class ResearchEndpoints
{
    public const int TreeGameLimit = 50, SearchGameLimit = 200;

    /// <summary>One index row that matched, with the little of its Game the counts need.</summary>
    private sealed record Hit(int GameId, int Ply, string? Uci, GameResult Result, OwnerColour OwnerColour, string? Date, DateTime CreatedAt);

    /// <summary>Maps GET /api/research/tree and GET /api/research/search, both taking ?fen=.</summary>
    public static void MapResearchEndpoints(this IEndpointRouteBuilder app)
    {
        var research = app.MapGroup("/api/research");

        research.MapGet("/tree", async (string? fen, CollectionDbContext db) =>
        {
            if (PositionInput.Read(fen, out var position) is { } problem) return problem;
            var hits = await HitsAsync(db, position);
            var moves = hits
                .Where(h => h.Uci is not null)
                .GroupBy(h => h.Uci!)
                .Select(g => ToTreeMove(position, g.Key, [.. g.DistinctBy(h => h.GameId)]))
                .OfType<TreeMoveDto>()
                .OrderByDescending(m => m.GameCount).ThenBy(m => m.San, StringComparer.Ordinal)
                .ToList();
            var (count, games) = await NewestGamesAsync(db, hits, TreeGameLimit);
            return Results.Ok(new OpeningTreeDto(count, moves, games));
        });

        research.MapGet("/search", async (string? fen, CollectionDbContext db) =>
        {
            if (PositionInput.Read(fen, out var position) is { } problem) return problem;
            var (count, games) = await NewestGamesAsync(db, await HitsAsync(db, position), SearchGameLimit);
            return Results.Ok(new PositionSearchDto(count, games));
        });
    }

    /// <summary>Every index row for the position, joined to its Game's result, colour and date. No full Games are loaded.</summary>
    private static Task<List<Hit>> HitsAsync(CollectionDbContext db, Position position)
    {
        var key = PositionIndex.Key(position);
        return (from r in db.PositionIndex
                where r.Hash == key
                join g in db.Games on r.GameId equals g.Id
                select new Hit(r.GameId, r.Ply, r.Uci, g.Result, g.OwnerColour, g.Date, g.CreatedAt))
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>The move with its counts, or null if it is not legal here (only possible after a hash collision).</summary>
    private static TreeMoveDto? ToTreeMove(Position position, string uci, List<Hit> games)
    {
        if (!Move.TryParseUci(uci, out var move) || !position.IsLegal(move)) return null;
        var results = games.Select(g => g.Result.ForOwner(g.OwnerColour)).ToList();
        int wins = results.Count(r => r == OwnerResult.Win), draws = results.Count(r => r == OwnerResult.Draw);
        var score = Math.Round((wins + draws / 2.0) / games.Count * 100, 1);
        return new TreeMoveDto(uci, position.ToSan(move), position.Play(move).ToFen(), games.Count, wins, draws,
            games.Count - wins - draws, score);
    }

    /// <summary>
    /// How many Games reached the position, and the newest of them (each at the first time it
    /// reached it) with their list rows. Only the Games returned are loaded in full.
    /// </summary>
    private static async Task<(int Count, List<PositionGameDto> Games)> NewestGamesAsync(CollectionDbContext db, List<Hit> hits, int limit)
    {
        var firsts = hits
            .GroupBy(h => h.GameId)
            .Select(g => g.MinBy(h => h.Ply)!)
            .OrderByDescending(h => h.Date ?? "").ThenByDescending(h => h.CreatedAt)
            .ToList();
        var shown = firsts.Take(limit).ToList();
        var ids = shown.Select(h => h.GameId).ToList();

        var ownerName = await GameQueries.OwnerNameAsync(db);
        var analysed = (await db.EngineAssessments.Where(a => ids.Contains(a.GameId)).Select(a => a.GameId).ToListAsync()).ToHashSet();
        var games = await db.Games.WithDetails().Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id);
        var list = shown
            .Select(h => new PositionGameDto(GameQueries.ToSummary(games[h.GameId], ownerName, analysed.Contains(h.GameId)), h.Ply, h.Ply / 2 + 1))
            .ToList();
        return (firsts.Count, list);
    }
}
