using ChessDatabase.Api.Collection;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Games;

/// <summary>The HTTP side of changing a Game's moves; <see cref="GameMoves"/> does the work.</summary>
public static class MoveTreeEndpoints
{
    public static void MapMoveTreeEndpoints(this RouteGroupBuilder games)
    {
        games.MapPost("/{id:int}/moves", (int id, AddMoveRequest body, GameMoves moves, CollectionDbContext db) =>
            AddLineAsync(moves, db, id, body.ParentId, [body.Move], "move"));

        // A whole line at once, e.g. an Engine line the Owner clicked.
        games.MapPost("/{id:int}/moves/line", (int id, AddLineRequest body, GameMoves moves, CollectionDbContext db) =>
            AddLineAsync(moves, db, id, body.ParentId, body.Moves ?? [], "moves"));

        games.MapPost("/{id:int}/moves/{nodeId:int}/promote", (int id, int nodeId, GameMoves moves, CollectionDbContext db) =>
            ChangeAsync(moves, db, id, edit => edit.PromoteAsync(nodeId)));

        games.MapDelete("/{id:int}/moves/{nodeId:int}/variation", (int id, int nodeId, GameMoves moves, CollectionDbContext db) =>
            ChangeAsync(moves, db, id, edit => edit.DeleteVariationAsync(nodeId)));

        // Delete from here: the move and everything after it.
        games.MapDelete("/{id:int}/moves/{nodeId:int}", (int id, int nodeId, GameMoves moves, CollectionDbContext db) =>
            ChangeAsync(moves, db, id, edit => edit.DeleteFromAsync(nodeId)));
    }

    /// <summary>Plays a line from the parent; answers with its last move and the Game.</summary>
    private static async Task<IResult> AddLineAsync(
        GameMoves moves, CollectionDbContext db, int gameId, int? parentId, IReadOnlyList<string?> line, string field)
    {
        await using var edit = await moves.OpenAsync(gameId);
        if (edit is null) return Results.NotFound();
        var change = await edit.AddLineAsync(parentId, line);
        if (change.Problem is { } problem)
            return problem == MoveChangeProblem.NotInGame
                ? Invalid("parentId", "That move is not part of this Game.")
                : Invalid(field, Message(change));
        await edit.SaveAsync();
        return Results.Ok(new AddMoveResponse(change.Last!.Id, (await GameQueries.LoadGameDtoAsync(db, gameId))!));
    }

    /// <summary>A change to one move of the Game; a move that isn't there is not found.</summary>
    private static async Task<IResult> ChangeAsync(
        GameMoves moves, CollectionDbContext db, int gameId, Func<GameMovesEdit, Task<MoveChange>> change)
    {
        await using var edit = await moves.OpenAsync(gameId);
        if (edit is null) return Results.NotFound();
        var result = await change(edit);
        if (result.Problem is { } problem)
            return problem == MoveChangeProblem.NotInGame ? Results.NotFound() : Invalid("nodeId", Message(result));
        await edit.SaveAsync();
        return Results.Ok(await GameQueries.LoadGameDtoAsync(db, gameId));
    }

    /// <summary>What the Owner is told when a change could not be made.</summary>
    private static string Message(MoveChange change) => change.Problem switch
    {
        MoveChangeProblem.NotInGame => "That move is not part of this Game.",
        MoveChangeProblem.NoMoves => "Give at least one move.",
        MoveChangeProblem.IllegalMove => $"'{change.IllegalMove}' is not a legal move here.",
        MoveChangeProblem.OnMainLine => "That move is on the main line, not in a Variation.",
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };
}
