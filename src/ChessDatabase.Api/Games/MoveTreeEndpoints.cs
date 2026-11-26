using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Openings;
using ChessDatabase.Rules;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Games;

/// <summary>Operations on a Game's move tree. Every change is checked by Chess Rules and saved at once.</summary>
public static class MoveTreeEndpoints
{
    public static void MapMoveTreeEndpoints(this RouteGroupBuilder games)
    {
        games.MapPost("/{id:int}/moves", async (int id, AddMoveRequest body, CollectionDbContext db, OpeningClassifier openings) =>
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
                await MainLine.RefreshAsync(db, openings, id);
            }

            return Results.Ok(new AddMoveResponse(node.Id, (await GameQueries.LoadGameDtoAsync(db, id))!));
        });

        games.MapPost("/{id:int}/moves/{nodeId:int}/promote", async (int id, int nodeId, CollectionDbContext db, OpeningClassifier openings) =>
        {
            if (await LoadTreeAsync(db, id, nodeId) is not { } tree) return Results.NotFound();
            tree.PromoteToMainLine(nodeId);
            await db.SaveChangesAsync();
            await MainLine.RefreshAsync(db, openings, id);
            return Results.Ok(await GameQueries.LoadGameDtoAsync(db, id));
        });

        games.MapDelete("/{id:int}/moves/{nodeId:int}/variation", async (int id, int nodeId, CollectionDbContext db, OpeningClassifier openings) =>
        {
            if (await LoadTreeAsync(db, id, nodeId) is not { } tree) return Results.NotFound();
            if (tree.VariationStart(nodeId) is not { } start)
                return Invalid("nodeId", "That move is on the main line, not in a Variation.");
            await DeleteSubtreeAsync(db, tree, start);
            await MainLine.RefreshAsync(db, openings, id);
            return Results.Ok(await GameQueries.LoadGameDtoAsync(db, id));
        });

        // Delete from here: the move and everything after it. If it was the main continuation, the
        // next Variation at that point (if any) becomes the main line.
        games.MapDelete("/{id:int}/moves/{nodeId:int}", async (int id, int nodeId, CollectionDbContext db, OpeningClassifier openings) =>
        {
            if (await LoadTreeAsync(db, id, nodeId) is not { } tree) return Results.NotFound();
            await DeleteSubtreeAsync(db, tree, nodeId);
            await MainLine.RefreshAsync(db, openings, id);
            return Results.Ok(await GameQueries.LoadGameDtoAsync(db, id));
        });
    }

    /// <summary>Loads the Game's tree (tracked, so order changes save), or null if the Game or node is not there.</summary>
    private static async Task<MoveTree?> LoadTreeAsync(CollectionDbContext db, int gameId, int nodeId)
    {
        var nodes = await db.MoveNodes.Where(n => n.GameId == gameId).ToListAsync();
        var tree = new MoveTree(nodes);
        return tree.Contains(nodeId) ? tree : null;
    }

    /// <summary>Deletes through the change tracker, so the removed nodes don't linger in this context.</summary>
    private static async Task DeleteSubtreeAsync(CollectionDbContext db, MoveTree tree, int nodeId)
    {
        db.MoveNodes.RemoveRange(tree.Subtree(nodeId).Select(id => tree.Node(id)));
        await db.SaveChangesAsync();
    }

    private static bool TryReadMove(Position position, string? text, out Move move)
    {
        text = text?.Trim();
        if (Move.TryParseUci(text, out move) && position.IsLegal(move)) return true;
        return position.TryParseSan(text, out move);
    }
}
