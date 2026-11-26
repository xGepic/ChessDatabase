using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Research;

/// <summary>A move played from the position in the Owner's Games.</summary>
/// <param name="Games">Games in which it was played from this position.</param>
/// <param name="Score">The Owner's score in those Games, in percent (a draw counts half).</param>
public sealed record TreeMoveDto(string Uci, string San, int Games, int Wins, int Draws, int Losses, double Score);

/// <summary>A Game that reached the position in its main line.</summary>
/// <param name="Ply">Half-moves played when the position first occurred (0: the starting position).</param>
/// <param name="MoveNumber">The number of the move played next from the position.</param>
public sealed record PositionGameDto(GameSummaryDto Game, int Ply, int MoveNumber);

/// <param name="Games">Games that reached the position.</param>
/// <param name="Moves">Most played first.</param>
/// <param name="GameList">Newest first.</param>
public sealed record OpeningTreeDto(int Games, IReadOnlyList<TreeMoveDto> Moves, IReadOnlyList<PositionGameDto> GameList);

/// <summary>
/// Research over the position index: the Opening Tree (moves played from a position in the
/// Owner's Games) and Position Search (Games whose main line reached a position). Only main-line
/// positions are indexed, so Variations never count. The position is given as a FEN; positions
/// reached by different move orders are the same position.
/// </summary>
public static class ResearchEndpoints
{
    public static void MapResearchEndpoints(this IEndpointRouteBuilder app)
    {
        var research = app.MapGroup("/api/research");

        research.MapGet("/tree", async (string? fen, CollectionDbContext db) =>
        {
            if (!TryReadPosition(fen, out var position, out var problem)) return problem;
            var (rows, games) = await ReachedAsync(db, position);

            var moves = rows
                .Where(r => r.Uci is not null)
                .GroupBy(r => r.Uci!)
                .Select(g => (Uci: g.Key, Games: g.Select(r => games[r.GameId].Game).DistinctBy(x => x.Id).ToList()))
                .Where(m => Move.TryParseUci(m.Uci, out var move) && position.IsLegal(move))
                .Select(m => ToTreeMove(position, m.Uci, m.Games))
                .OrderByDescending(m => m.Games).ThenBy(m => m.San, StringComparer.Ordinal)
                .ToList();
            return Results.Ok(new OpeningTreeDto(games.Count, moves, GameList(rows, games)));
        });

        research.MapGet("/search", async (string? fen, CollectionDbContext db) =>
        {
            if (!TryReadPosition(fen, out var position, out var problem)) return problem;
            var (rows, games) = await ReachedAsync(db, position);
            return Results.Ok(GameList(rows, games));
        });
    }

    private static bool TryReadPosition(string? fen, out Position position, out IResult problem)
    {
        position = Position.Initial;
        problem = Results.Ok();
        try
        {
            position = Position.FromFen(fen ?? "");
        }
        catch (FormatException e)
        {
            problem = Invalid("fen", e.Message);
            return false;
        }
        if (position.SetupProblem() is { } reason)
        {
            problem = Invalid("fen", reason);
            return false;
        }
        return true;
    }

    /// <summary>The index rows for the position and the Games they belong to, with their list rows.</summary>
    private static async Task<(List<PositionIndexRecord> Rows, Dictionary<int, (GameRecord Game, GameSummaryDto Row)> Games)> ReachedAsync(
        CollectionDbContext db, Position position)
    {
        var key = PositionIndex.Key(position);
        var rows = await db.PositionIndex.AsNoTracking().Where(r => r.Hash == key).ToListAsync();
        var ids = rows.Select(r => r.GameId).Distinct().ToList();
        var ownerName = await GameQueries.OwnerNameAsync(db);
        var analysed = (await db.EngineAssessments.Where(a => ids.Contains(a.GameId)).Select(a => a.GameId).ToListAsync()).ToHashSet();
        var games = await db.Games.WithDetails().Where(g => ids.Contains(g.Id)).ToListAsync();
        return (rows, games.ToDictionary(g => g.Id, g => (g, GameQueries.ToSummary(g, ownerName, analysed.Contains(g.Id)))));
    }

    private static TreeMoveDto ToTreeMove(Position position, string uci, List<GameRecord> games)
    {
        Move.TryParseUci(uci, out var move);
        var results = games.Select(g => g.ResultForOwner()).ToList();
        int wins = results.Count(r => r == OwnerResult.Win), draws = results.Count(r => r == OwnerResult.Draw);
        var score = Math.Round((wins + draws / 2.0) / games.Count * 100, 1);
        return new TreeMoveDto(uci, position.ToSan(move), games.Count, wins, draws, games.Count - wins - draws, score);
    }

    /// <summary>Each Game once, at the first time it reached the position; newest first.</summary>
    private static List<PositionGameDto> GameList(
        List<PositionIndexRecord> rows, Dictionary<int, (GameRecord Game, GameSummaryDto Row)> games) =>
        [.. rows
            .GroupBy(r => r.GameId)
            .Select(g => (Ply: g.Min(r => r.Ply), games[g.Key].Game, games[g.Key].Row))
            .OrderByDescending(x => x.Game.Date ?? "").ThenByDescending(x => x.Game.CreatedAt)
            .Select(x => new PositionGameDto(x.Row, x.Ply, x.Ply / 2 + 1))];
}
