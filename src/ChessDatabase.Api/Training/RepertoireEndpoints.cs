using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using Microsoft.EntityFrameworkCore;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Training;

/// <summary>A Repertoire as the API shows it: the same node shape as a Game's moves (comments as CommentAfter).</summary>
public sealed record RepertoireDto(OwnerColour Side, IReadOnlyList<int> FirstMoves, IReadOnlyList<MoveNodeDto> Nodes);

/// <param name="Moves">UCI (or SAN) moves played one after another from the parent; null parent = the starting position.</param>
public sealed record AddRepertoireLineRequest(int? ParentId, IReadOnlyList<string?>? Moves);

/// <param name="NodeId">The line's last move.</param>
public sealed record AddRepertoireLineResponse(int NodeId, RepertoireDto Repertoire);

public sealed record RepertoireCommentRequest(string? Text);

/// <summary>A Repertoire's move tree.</summary>
public sealed class RepertoireTree(OwnerColour side, IEnumerable<RepertoireNodeRecord> nodes) : MoveTree<RepertoireNodeRecord>(nodes)
{
    public RepertoireDto ToDto()
    {
        var (firstMoves, dtos) = ToDtos(n => (null, n.Comment, []));
        return new RepertoireDto(side, firstMoves, dtos);
    }
}

/// <summary>
/// The Owner's Repertoires, one per colour, edited like a Game's move tree: lines are added by
/// playing moves (a line that shares moves with the Repertoire merges into it), deleted, promoted
/// and commented. Every change is checked by Chess Rules and saved at once.
/// </summary>
public static class RepertoireEndpoints
{
    /// <summary>Maps /api/repertoire/{side}, side being "white" or "black".</summary>
    public static void MapRepertoireEndpoints(this IEndpointRouteBuilder app)
    {
        var repertoire = app.MapGroup("/api/repertoire/{side}");

        repertoire.MapGet("/", async (string side, CollectionDbContext db) =>
            ReadSide(side) is { } colour ? Results.Ok((await LoadAsync(db, colour)).ToDto()) : Results.NotFound());

        repertoire.MapPost("/lines", async (string side, AddRepertoireLineRequest body, CollectionDbContext db) =>
        {
            if (ReadSide(side) is not { } colour) return Results.NotFound();
            var tree = await LoadAsync(db, colour);
            if (body.ParentId is { } parentId && !tree.Contains(parentId))
                return Invalid("parentId", "That move is not part of this Repertoire.");
            var moves = body.Moves ?? [];
            if (moves.Count == 0) return Invalid("moves", "Give at least one move.");
            if (MoveTreeEndpoints.ReadLine(tree.PositionAfter(body.ParentId), moves, out var line) is { } illegal)
                return Invalid("moves", $"'{illegal}' is not a legal move here.");

            var (last, added) = tree.AddLine(body.ParentId, line, m =>
            {
                var node = new RepertoireNodeRecord { Side = colour, ParentId = m.ParentId, Parent = m.Parent, Order = m.Order, Uci = m.Uci, San = m.San };
                db.RepertoireNodes.Add(node);
                return node;
            });
            if (added) await db.SaveChangesAsync();
            return Results.Ok(new AddRepertoireLineResponse(last.Id, (await LoadAsync(db, colour)).ToDto()));
        });

        // Delete from here: the move and everything after it.
        repertoire.MapDelete("/nodes/{nodeId:int}", (string side, int nodeId, CollectionDbContext db) =>
            EditNodeAsync(db, side, nodeId, async tree =>
            {
                await DeleteAsync(db, tree, nodeId);
                return null;
            }));

        repertoire.MapDelete("/nodes/{nodeId:int}/variation", (string side, int nodeId, CollectionDbContext db) =>
            EditNodeAsync(db, side, nodeId, async tree =>
            {
                if (tree.VariationStart(nodeId) is not { } start) return Invalid("nodeId", "That move is on the main line, not in a Variation.");
                await DeleteAsync(db, tree, start);
                return null;
            }));

        repertoire.MapPost("/nodes/{nodeId:int}/promote", (string side, int nodeId, CollectionDbContext db) =>
            EditNodeAsync(db, side, nodeId, async tree =>
            {
                tree.PromoteToMainLine(nodeId);
                await db.SaveChangesAsync();
                return null;
            }));

        repertoire.MapPut("/nodes/{nodeId:int}/comment", (string side, int nodeId, RepertoireCommentRequest body, CollectionDbContext db) =>
            EditNodeAsync(db, side, nodeId, async tree =>
            {
                var text = body.Text?.Trim() ?? "";
                if (text.Length > GameDetailLimits.Notes) return Invalid("text", $"A comment can be at most {GameDetailLimits.Notes} characters.");
                tree.Node(nodeId).Comment = text.Length > 0 ? text : null;
                await db.SaveChangesAsync();
                return null;
            }));
    }

    /// <summary>
    /// Runs a change to one node of a side's Repertoire: 404 for an unknown side or a node not in
    /// that Repertoire, the change's own problem if it has one, else the Repertoire after the change.
    /// </summary>
    private static async Task<IResult> EditNodeAsync(CollectionDbContext db, string side, int nodeId, Func<RepertoireTree, Task<IResult?>> change)
    {
        if (ReadSide(side) is not { } colour) return Results.NotFound();
        var tree = await LoadAsync(db, colour);
        if (!tree.Contains(nodeId)) return Results.NotFound();
        if (await change(tree) is { } problem) return problem;
        return Results.Ok((await LoadAsync(db, colour)).ToDto());
    }

    private static OwnerColour? ReadSide(string side) => side.ToLowerInvariant() switch
    {
        "white" => OwnerColour.White,
        "black" => OwnerColour.Black,
        _ => null,
    };

    /// <summary>The side's tree, tracked: promoting changes nodes in place, deleting removes them.</summary>
    private static async Task<RepertoireTree> LoadAsync(CollectionDbContext db, OwnerColour side) =>
        new(side, await db.RepertoireNodes.Where(n => n.Side == side).ToListAsync());

    private static async Task DeleteAsync(CollectionDbContext db, RepertoireTree tree, int nodeId)
    {
        db.RepertoireNodes.RemoveRange(tree.Subtree(nodeId).Select(tree.Node));
        await db.SaveChangesAsync();
    }
}
