using ChessDatabase.Api.Collection;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Games;

/// <summary>Annotations: a text comment after (or before) a move, and symbols as NAGs. Saved at once.</summary>
public static class AnnotationEndpoints
{
    public const int MaxCommentLength = 10_000;

    public static void MapAnnotationEndpoints(this RouteGroupBuilder games)
    {
        games.MapPut("/{id:int}/moves/{nodeId:int}/comment", async (int id, int nodeId, SetCommentRequest body, CollectionDbContext db) =>
        {
            if (await FindNodeAsync(db, id, nodeId) is not { } node) return Results.NotFound();
            var text = body.Text?.Trim();
            if (text?.Length > MaxCommentLength)
                return Invalid("text", $"A comment can be at most {MaxCommentLength} characters long.");

            var value = string.IsNullOrEmpty(text) ? null : text;
            if (body.Placement == CommentPlacement.Before) node.CommentBefore = value;
            else node.CommentAfter = value;
            await db.SaveChangesAsync();
            return Results.Ok(await GameEndpoints.LoadGameDtoAsync(db, id));
        });

        games.MapPut("/{id:int}/moves/{nodeId:int}/nags", async (int id, int nodeId, SetNagsRequest body, CollectionDbContext db) =>
        {
            if (await FindNodeAsync(db, id, nodeId) is not { } node) return Results.NotFound();
            var nags = body.Nags ?? [];
            if (Nags.Validate(nags) is { } problem) return Invalid("nags", problem);

            node.Nags = [.. nags.Distinct()];
            await db.SaveChangesAsync();
            return Results.Ok(await GameEndpoints.LoadGameDtoAsync(db, id));
        });
    }

    private static Task<MoveNodeRecord?> FindNodeAsync(CollectionDbContext db, int gameId, int nodeId) =>
        db.MoveNodes.SingleOrDefaultAsync(n => n.Id == nodeId && n.GameId == gameId);
}
