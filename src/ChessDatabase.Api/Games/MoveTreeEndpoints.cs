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
        games.MapPost("/{id:int}/moves", (int id, AddMoveRequest body, CollectionDbContext db, OpeningClassifier openings) =>
            AddLineAsync(db, openings, id, body.ParentId, [body.Move], "move"));

        // A whole line at once, e.g. an Engine line the Owner clicked.
        games.MapPost("/{id:int}/moves/line", (int id, AddLineRequest body, CollectionDbContext db, OpeningClassifier openings) =>
            AddLineAsync(db, openings, id, body.ParentId, body.Moves ?? [], "moves"));

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

    /// <summary>
    /// Plays the moves one after another from the parent. Where a move is already recorded there,
    /// it just moves along it; the first new move starts a new line (a Variation if the position
    /// already had a continuation). Every move is checked before anything is saved.
    /// </summary>
    private static async Task<IResult> AddLineAsync(
        CollectionDbContext db, OpeningClassifier openings, int gameId, int? parentId, IReadOnlyList<string?> moves, string field)
    {
        var game = await db.Games.Include(g => g.MoveNodes).SingleOrDefaultAsync(g => g.Id == gameId);
        if (game is null) return Results.NotFound();
        var tree = new MoveTree(game.MoveNodes);
        if (parentId is { } id && !tree.Contains(id))
            return Invalid("parentId", "That move is not part of this Game.");
        if (moves.Count == 0) return Invalid(field, "Give at least one move.");

        var position = tree.PositionAfter(parentId);
        var line = new List<(string Uci, string San)>();
        foreach (var text in moves)
        {
            if (!TryReadMove(position, text, out var move))
                return Invalid(field, $"'{text}' is not a legal move here.");
            line.Add((move.ToUci(), position.ToSan(move)));
            position = position.Play(move);
        }

        var (last, added) = AddLine(game, tree, parentId, line);

        if (added)
        {
            await db.SaveChangesAsync();
            await MainLine.RefreshAsync(db, openings, gameId);
        }
        return Results.Ok(new AddMoveResponse(last.Id, (await GameQueries.LoadGameDtoAsync(db, gameId))!));
    }

    /// <summary>
    /// Adds checked moves (UCI and SAN) to the Game's tracked nodes, one after another from the parent:
    /// a move already recorded there is followed, the first new one starts a new line after the
    /// existing continuations. Nothing is saved. Returns the line's last node and whether anything is new.
    /// </summary>
    public static (MoveNodeRecord Last, bool Added) AddLine(
        GameRecord game, MoveTree tree, int? parentId, IReadOnlyList<(string Uci, string San)> line)
    {
        if (line.Count == 0) throw new ArgumentException("A line has at least one move.", nameof(line));
        int? at = parentId;
        MoveNodeRecord? last = null, firstNew = null;
        foreach (var (uci, san) in line)
        {
            if (firstNew is null && tree.ChildrenOf(at).FirstOrDefault(n => n.Uci == uci) is { } existing)
            {
                last = existing;
                at = existing.Id;
                continue;
            }
            var node = new MoveNodeRecord { GameId = game.Id, Uci = uci, San = san };
            if (firstNew is null)
            {
                var siblings = tree.ChildrenOf(at);
                node.ParentId = at;
                node.Order = siblings.Count == 0 ? 0 : siblings.Max(n => n.Order) + 1;
                firstNew = node;
            }
            else node.Parent = last;
            game.MoveNodes.Add(node);
            last = node;
        }
        return (last!, firstNew is not null);
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
