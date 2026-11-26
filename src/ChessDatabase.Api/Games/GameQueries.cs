using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Players;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>Reading Games from the Collection the way the API shows them.</summary>
public static class GameQueries
{
    /// <summary>Games with their Opponent, Event and Tags, and optionally the move tree.</summary>
    public static IQueryable<GameRecord> WithDetails(this IQueryable<GameRecord> games, bool withMoves = false)
    {
        IQueryable<GameRecord> query = games.AsNoTracking().Include(g => g.Opponent).Include(g => g.Event).Include(g => g.Tags);
        if (withMoves) query = query.Include(g => g.MoveNodes);
        return query.AsSplitQuery();
    }

    public static async Task<string> OwnerNameAsync(CollectionDbContext db)
    {
        var name = (await db.Settings.AsNoTracking().SingleOrDefaultAsync())?.OwnerName;
        return string.IsNullOrWhiteSpace(name) ? "Owner" : name;
    }

    /// <param name="analysed">Whether the Game has an Engine Assessment.</param>
    public static GameSummaryDto ToSummary(GameRecord game, string ownerName, bool analysed)
    {
        var opponent = new PlayerDto(game.Opponent.Id, game.Opponent.Name);
        var (white, black) = PlayerSides.ToWhiteAndBlack(game.OwnerColour, ownerName, opponent.Name);
        var gameEvent = game.Event is null ? null : new EventDto(game.Event.Id, game.Event.Name);
        return new GameSummaryDto(game.Id, game.Date, game.OwnerColour, opponent, white, black,
            game.WhiteElo, game.BlackElo, game.Result.ToPgn(), gameEvent, game.Eco, game.OpeningName, analysed);
    }

    public static async Task<GameDto?> LoadGameDtoAsync(CollectionDbContext db, int id)
    {
        var game = await db.Games.WithDetails(withMoves: true).SingleOrDefaultAsync(g => g.Id == id);
        if (game is null) return null;

        var summary = ToSummary(game, await OwnerNameAsync(db), await db.EngineAssessments.AnyAsync(a => a.GameId == id));
        var (firstMoves, nodes) = new MoveTree(game.MoveNodes).ToDtos();
        return new GameDto(summary, game, MoveTree.StartPosition.ToFen(), firstMoves, nodes);
    }
}
