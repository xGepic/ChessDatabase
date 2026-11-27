using ChessDatabase.Api.Assessment;
using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using static ChessDatabase.Api.ApiResults;

namespace ChessDatabase.Api.Analysis;

/// <param name="Copied">How many moves gained something: a symbol, a Variation or both.</param>
public sealed record CopyResultDto(int Copied, GameDto Game);

/// <summary>
/// The only route from Engine Assessment to Annotations (ADR 0003): on the Owner's request, a
/// classified move gets its symbol (?!, ?, ??) and the Engine's line becomes a Variation beside it.
/// A move symbol the Owner already set is kept, comments are never touched, and a line already in
/// the Game is followed rather than added again.
/// </summary>
public static class CopyAssessmentEndpoints
{
    public static void MapCopyAssessmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/games/{id:int}/assessment/moves/{ply:int}/copy", async (int id, int ply, CollectionDbContext db, GameMoves gameMoves) =>
        {
            if (await AssessmentQueries.LoadAsync(db, id) is not { } assessment) return Results.NotFound();
            if (assessment.Moves.FirstOrDefault(m => m.Ply == ply) is not { } move) return Results.NotFound();
            if (move.Classification is null) return Invalid("ply", "The Engine had nothing against that move.");
            if (move.NodeId is null) return Invalid("ply", "That move has changed since the analysis. Analyse the Game again.");

            var copied = await CopyAsync(gameMoves, id, [move]);
            return Results.Ok(new CopyResultDto(copied, (await GameQueries.LoadGameDtoAsync(db, id))!));
        });

        // "Copy all mistakes and blunders": inaccuracies stay out.
        app.MapPost("/api/games/{id:int}/assessment/copy", async (int id, CollectionDbContext db, GameMoves gameMoves) =>
        {
            if (await AssessmentQueries.LoadAsync(db, id) is not { } assessment) return Results.NotFound();
            var moves = assessment.Moves
                .Where(m => m.NodeId is not null && m.Classification is MoveClassification.Mistake or MoveClassification.Blunder)
                .ToList();
            var copied = await CopyAsync(gameMoves, id, moves);
            return Results.Ok(new CopyResultDto(copied, (await GameQueries.LoadGameDtoAsync(db, id))!));
        });
    }

    /// <summary>Copies the moves and returns how many of them changed anything.</summary>
    private static async Task<int> CopyAsync(GameMoves gameMoves, int gameId, List<AssessedMoveDto> moves)
    {
        if (moves.Count == 0) return 0;
        await using var edit = (await gameMoves.OpenAsync(gameId))!;
        var copied = 0;
        foreach (var move in moves)
        {
            if (edit.Node(move.NodeId!.Value) is not { } node) continue; // changed meanwhile
            var changed = false;
            if (!node.Nags.Any(Nags.IsMoveSymbol))
            {
                node.Nags = [SymbolFor(move.Classification!.Value), .. node.Nags];
                changed = true;
            }
            if (move.BestLineUci.Count > 0 && move.BestLineUci[0] != node.Uci)
                changed |= (await edit.AddLineAsync(node.ParentId, [.. move.BestLineUci])).Changed;
            if (changed) copied++;
        }
        await edit.SaveAsync();
        return copied;
    }

    private static int SymbolFor(MoveClassification classification) => classification switch
    {
        MoveClassification.Inaccuracy => Nags.Dubious,
        MoveClassification.Mistake => Nags.Mistake,
        _ => Nags.Blunder,
    };
}
