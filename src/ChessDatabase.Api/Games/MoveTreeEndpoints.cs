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

        games.MapPost("/{id:int}/moves/{nodeId:int}/promote", async (int id, int nodeId, CollectionDbContext db) =>
        {
            if (await LoadTreeAsync(db, id, nodeId) is not { } tree) return Results.NotFound();
            tree.PromoteToMainLine(nodeId);
            await db.SaveChangesAsync();
            return Results.Ok(await GameEndpoints.LoadGameDtoAsync(db, id));
        });

        games.MapDelete("/{id:int}/moves/{nodeId:int}/variation", async (int id, int nodeId, CollectionDbContext db) =>
        {
            if (await LoadTreeAsync(db, id, nodeId) is not { } tree) return Results.NotFound();
            if (tree.VariationStart(nodeId) is not { } start)
                return Invalid("nodeId", "That move is on the main line, not in a Variation.");
            await DeleteSubtreeAsync(db, tree, start);
            return Results.Ok(await GameEndpoints.LoadGameDtoAsync(db, id));
        });

        // Delete from here: the move and everything after it.
        games.MapDelete("/{id:int}/moves/{nodeId:int}", async (int id, int nodeId, CollectionDbContext db) =>
        {
            if (await LoadTreeAsync(db, id, nodeId) is not { } tree) return Results.NotFound();
            await DeleteSubtreeAsync(db, tree, nodeId);
            return Results.Ok(await GameEndpoints.LoadGameDtoAsync(db, id));
        });
    }

    public const int MaxCommentLength = 10_000;

    /// <summary>Annotations: a text comment after (or before) a move, and symbols as NAGs.</summary>
    public static void MapAnnotationEndpoints(this RouteGroupBuilder games)
    {
        games.MapPut("/{id:int}/moves/{nodeId:int}/comment", async (int id, int nodeId, SetCommentRequest body, CollectionDbContext db) =>
        {
            var node = await db.MoveNodes.SingleOrDefaultAsync(n => n.Id == nodeId && n.GameId == id);
            if (node is null) return Results.NotFound();
            var text = body.Text?.Trim();
            if (text?.Length > MaxCommentLength)
                return Invalid("text", $"A comment can be at most {MaxCommentLength} characters long.");

            var value = string.IsNullOrEmpty(text) ? null : text;
            switch (body.Placement)
            {
                case CommentPlacement.Before: node.CommentBefore = value; break;
                default: node.CommentAfter = value; break;
            }
            await db.SaveChangesAsync();
            return Results.Ok(await GameEndpoints.LoadGameDtoAsync(db, id));
        });

        games.MapPut("/{id:int}/moves/{nodeId:int}/nags", async (int id, int nodeId, SetNagsRequest body, CollectionDbContext db) =>
        {
            var node = await db.MoveNodes.SingleOrDefaultAsync(n => n.Id == nodeId && n.GameId == id);
            if (node is null) return Results.NotFound();
            var nags = body.Nags ?? [];
            if (nags.Any(n => n is < 1 or > 255))
                return Invalid("nags", "Symbols are NAG numbers from 1 to 255.");

            node.Nags = string.Join(' ', nags.Distinct());
            await db.SaveChangesAsync();
            return Results.Ok(await GameEndpoints.LoadGameDtoAsync(db, id));
        });
    }

    /// <summary>Loads the Game's tree (tracked, so order changes save), or null if the Game or node is not there.</summary>
    private static async Task<MoveTree?> LoadTreeAsync(CollectionDbContext db, int gameId, int nodeId)
    {
        var nodes = await db.MoveNodes.Where(n => n.GameId == gameId).ToListAsync();
        var tree = new MoveTree(nodes);
        return tree.Contains(nodeId) ? tree : null;
    }

    private static async Task DeleteSubtreeAsync(CollectionDbContext db, MoveTree tree, int nodeId)
    {
        var ids = tree.Subtree(nodeId);
        await db.MoveNodes.Where(n => ids.Contains(n.Id)).ExecuteDeleteAsync();
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
