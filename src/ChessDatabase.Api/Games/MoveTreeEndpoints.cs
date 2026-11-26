using ChessDatabase.Api.Collection;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;

namespace ChessDatabase.Api.Games;

/// <summary>Operations on a Game's move tree. Every change is checked by Chess Rules and saved at once.</summary>
public static class MoveTreeEndpoints
{
    public static void MapMoveTreeEndpoints(this RouteGroupBuilder games)
    {
        games.MapPost("/{id:int}/moves", async (int id, AddMoveRequest body, CollectionDbContext db) =>
        {
            var game = await db.Games.Include(g => g.MoveNodes).SingleOrDefaultAsync(g => g.Id == id);
            if (game is null) return Results.NotFound();
            var tree = new MoveTree(game.MoveNodes);

            if (body.ParentId is { } parentId && !tree.Contains(parentId))
                return Invalid("parentId", "That move is not part of this Game.");

            var position = tree.PositionAfter(body.ParentId);
            if (!TryReadMove(position, body.Move, out var move))
                return Invalid("move", $"'{body.Move}' is not a legal move here.");

            // Playing the recorded continuation again just moves along it; anything else becomes a new line.
            var siblings = tree.ChildrenOf(body.ParentId);
            var node = siblings.FirstOrDefault(n => n.Uci == move.ToUci());
            if (node is null)
            {
                node = new MoveNodeRecord
                {
                    GameId = id,
                    ParentId = body.ParentId,
                    Order = siblings.Count == 0 ? 0 : siblings.Max(n => n.Order) + 1,
                    Uci = move.ToUci(),
                    San = position.ToSan(move),
                };
                game.MoveNodes.Add(node);
                await db.SaveChangesAsync();
            }

            return Results.Ok(new AddMoveResponse(node.Id, (await GameEndpoints.LoadGameDtoAsync(db, id))!));
        });
    }

    private static bool TryReadMove(Position position, string? text, out Move move)
    {
        text = text?.Trim();
        if (Move.TryParseUci(text, out move) && position.IsLegal(move)) return true;
        return position.TryParseSan(text, out move);
    }

    internal static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
