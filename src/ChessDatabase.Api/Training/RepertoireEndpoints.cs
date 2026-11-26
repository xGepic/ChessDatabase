using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using ChessDatabase.Rules;
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

            var position = tree.PositionAfter(body.ParentId);
            var line = new List<(string Uci, string San)>();
            foreach (var text in moves)
            {
                if (!MoveTreeEndpoints.TryReadMove(position, text, out var move)) return Invalid("moves", $"'{text}' is not a legal move here.");
                line.Add((move.ToUci(), position.ToSan(move)));
                position = position.Play(move);
            }

            var (last, added) = tree.AddLine(body.ParentId, line, (existingParent, newParent, order, uci, san) =>
            {
                var node = new RepertoireNodeRecord { Side = colour, ParentId = existingParent, Parent = newParent, Order = order, Uci = uci, San = san };
                db.RepertoireNodes.Add(node);
                return node;
            });
            if (added) await db.SaveChangesAsync();
            return Results.Ok(new AddRepertoireLineResponse(last.Id, (await LoadAsync(db, colour)).ToDto()));
        });

        // Delete from here: the move and everything after it.
        repertoire.MapDelete("/nodes/{nodeId:int}", async (string side, int nodeId, CollectionDbContext db) =>
        {
            if (ReadSide(side) is not { } colour) return Results.NotFound();
            var tree = await LoadAsync(db, colour);
            if (!tree.Contains(nodeId)) return Results.NotFound();
            await DeleteAsync(db, tree, nodeId);
            return Results.Ok((await LoadAsync(db, colour)).ToDto());
        });

        repertoire.MapDelete("/nodes/{nodeId:int}/variation", async (string side, int nodeId, CollectionDbContext db) =>
        {
            if (ReadSide(side) is not { } colour) return Results.NotFound();
            var tree = await LoadAsync(db, colour);
            if (!tree.Contains(nodeId)) return Results.NotFound();
            if (tree.VariationStart(nodeId) is not { } start) return Invalid("nodeId", "That move is on the main line, not in a Variation.");
            await DeleteAsync(db, tree, start);
            return Results.Ok((await LoadAsync(db, colour)).ToDto());
        });

        repertoire.MapPost("/nodes/{nodeId:int}/promote", async (string side, int nodeId, CollectionDbContext db) =>
        {
            if (ReadSide(side) is not { } colour) return Results.NotFound();
            var tree = await LoadAsync(db, colour);
            if (!tree.Contains(nodeId)) return Results.NotFound();
            tree.PromoteToMainLine(nodeId);
            await db.SaveChangesAsync();
            return Results.Ok((await LoadAsync(db, colour)).ToDto());
        });

        repertoire.MapPut("/nodes/{nodeId:int}/comment", async (string side, int nodeId, RepertoireCommentRequest body, CollectionDbContext db) =>
        {
            if (ReadSide(side) is not { } colour) return Results.NotFound();
            var node = await db.RepertoireNodes.SingleOrDefaultAsync(n => n.Id == nodeId && n.Side == colour);
            if (node is null) return Results.NotFound();
            var text = body.Text?.Trim() ?? "";
            if (text.Length > GameDetailLimits.Notes) return Invalid("text", $"A comment can be at most {GameDetailLimits.Notes} characters.");
            node.Comment = text.Length > 0 ? text : null;
            await db.SaveChangesAsync();
            return Results.Ok((await LoadAsync(db, colour)).ToDto());
        });
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
